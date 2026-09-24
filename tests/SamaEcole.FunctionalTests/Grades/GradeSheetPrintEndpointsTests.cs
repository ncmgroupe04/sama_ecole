using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Grades;

/// <summary>
/// Évolution N°1 — GET /grades/sheet/print : la fiche de saisie papier (PDF vierge), à travers la vraie
/// pile HTTP. Lecture ouverte comme la grille de saisie (Directeur, Secrétariat, Enseignant) ; la
/// composition de la fiche (ordre des élèves, barème) est testée en intégration.
/// </summary>
public class GradeSheetPrintEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public GradeSheetPrintEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record IdDto(Guid Id);

    private async Task<string> AccessTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<Guid> PostIdAsync(string url, string token, object body)
    {
        var response = await SendAsync(HttpMethod.Post, url, token, body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<IdDto>())!.Id;
    }

    /// <summary>Classe, matière, élève, année scolaire (et son premier trimestre) — le décor complet.</summary>
    private async Task<(Guid ClassroomId, Guid SubjectId, Guid TermId)> SeedAsync(string directeur)
    {
        var classroomId = await PostIdAsync("/api/v1/classrooms", directeur, new { name = "3e A", level = "Collège", capacity = 40 });
        var subjectId = await PostIdAsync("/api/v1/subjects", directeur, new { name = "Mathématiques", level = "Primaire", coefficient = 4 });
        await PostIdAsync("/api/v1/students", directeur,
            new { fullName = "Awa Fall", birthDate = "2015-01-01", birthPlace = "Dakar", gender = "F", classroomId });
        var yearId = await PostIdAsync("/api/v1/school-years", directeur,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" });

        var terms = await (await SendAsync(HttpMethod.Get, $"/api/v1/school-years/{yearId}/terms", directeur))
            .Content.ReadFromJsonAsync<List<IdDto>>();

        return (classroomId, subjectId, terms![0].Id);
    }

    private static string PrintUrl(Guid classroomId, Guid subjectId, Guid termId, string? evaluationType = "Devoir1") =>
        $"/api/v1/grades/sheet/print?classroomId={classroomId}&subjectId={subjectId}&termId={termId}"
        + (evaluationType is null ? "" : $"&evaluationType={evaluationType}");

    [Fact]
    public async Task The_Directeur_Gets_A_Pdf_Sheet_With_A_Named_File()
    {
        var directeur = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var (classroomId, subjectId, termId) = await SeedAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, PrintUrl(classroomId, subjectId, termId, "Composition"), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Contain("Fiche-Notes");
    }

    [Fact]
    public async Task The_Enseignant_And_The_Secretariat_Can_Print_The_Sheet_Too()
    {
        var directeur = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var (classroomId, subjectId, termId) = await SeedAsync(directeur);
        var url = PrintUrl(classroomId, subjectId, termId);

        var enseignant = await AccessTokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);
        var secretaire = await AccessTokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        (await SendAsync(HttpMethod.Get, url, enseignant)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, url, secretaire)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Finance_User_Cannot_Print_The_Sheet()
    {
        var directeur = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var (classroomId, subjectId, termId) = await SeedAsync(directeur);
        var finance = await AccessTokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

        var response = await SendAsync(HttpMethod.Get, PrintUrl(classroomId, subjectId, termId), finance);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_Callers_Are_Refused()
    {
        var response = await _client.GetAsync(PrintUrl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_Missing_Evaluation_Type_Is_Refused_Instead_Of_Defaulting_To_Devoir1()
    {
        var directeur = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var (classroomId, subjectId, termId) = await SeedAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, PrintUrl(classroomId, subjectId, termId, evaluationType: null), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_Unknown_Classroom_Returns_404()
    {
        var directeur = await AccessTokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
        var (_, subjectId, termId) = await SeedAsync(directeur);

        var response = await SendAsync(HttpMethod.Get, PrintUrl(Guid.NewGuid(), subjectId, termId), directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
