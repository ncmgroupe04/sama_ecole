using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Students;

/// <summary>
/// Dispense d'une matière obligatoire — GET|PUT /api/v1/class-subjects/students/{id}/exemptions, de bout en bout :
/// réservé au Directeur et au Secrétariat (le motif peut être médical), 401 sans jeton, 403 pour Finance et Enseignant,
/// 204 à l'écriture, 422 sans motif, 404 pour un élève inconnu.
/// </summary>
public class StudentExemptionsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public StudentExemptionsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record Created(Guid Id);
    private record ExemptibleSubject(Guid SubjectId, string Name, bool IsExempt, string? Reason, int GradeCount);
    private record ExemptionsDto(Guid StudentId, Guid SchoolYearId, List<ExemptibleSubject> Subjects);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> EnseignantAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>Année active, une matière « Éducation physique » et un élève d'une classe de même niveau.</summary>
    private async Task<(Guid Student, Guid Subject)> SeedAsync(string directeur)
    {
        (await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur,
            new { label = "2026-2027", startDate = "2026-10-01", endDate = "2027-06-30" })).StatusCode.Should().Be(HttpStatusCode.Created);

        var subject = await SendAsync(HttpMethod.Post, "/api/v1/subjects", directeur,
            new { name = "Éducation physique", level = "Primaire", coefficient = 1 });
        subject.StatusCode.Should().Be(HttpStatusCode.Created);

        var classroom = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2 A", level = "Primaire", capacity = 30 });
        classroom.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroomId = (await classroom.Content.ReadFromJsonAsync<Created>())!.Id;

        var student = await SendAsync(HttpMethod.Post, "/api/v1/students", directeur,
            new { fullName = "Awa Fall", birthDate = "2015-03-12", birthPlace = "Dakar", gender = "F", classroomId });
        student.StatusCode.Should().Be(HttpStatusCode.Created);

        return ((await student.Content.ReadFromJsonAsync<Created>())!.Id, (await subject.Content.ReadFromJsonAsync<Created>())!.Id);
    }

    private static string Url(Guid student) => $"/api/v1/class-subjects/students/{student}/exemptions";

    [Fact]
    public async Task Without_A_Token_Both_Routes_Return_401()
    {
        var student = Guid.NewGuid();

        (await SendAsync(HttpMethod.Get, Url(student), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Put, Url(student), null, new { exemptions = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Finance_And_Teachers_Cannot_Read_Or_Write_Exemptions()
    {
        var directeur = await DirecteurAsync();
        var (student, subject) = await SeedAsync(directeur);
        var body = new { exemptions = new[] { new { subjectId = subject, reason = "Inaptitude médicale" } } };

        foreach (var token in new[] { await FinanceAsync(), await EnseignantAsync() })
        {
            (await SendAsync(HttpMethod.Get, Url(student), token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await SendAsync(HttpMethod.Put, Url(student), token, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // Rien n'a été écrit par les refus.
        var read = await (await SendAsync(HttpMethod.Get, Url(student), directeur)).Content.ReadFromJsonAsync<ExemptionsDto>();
        read!.Subjects.Should().OnlyContain(s => !s.IsExempt);
    }

    [Fact]
    public async Task The_Director_And_The_Secretariat_Read_And_Write_Exemptions()
    {
        var directeur = await DirecteurAsync();
        var (student, subject) = await SeedAsync(directeur);

        var before = await SendAsync(HttpMethod.Get, Url(student), directeur);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        (await before.Content.ReadFromJsonAsync<ExemptionsDto>())!.Subjects
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new ExemptibleSubject(subject, "Éducation physique", false, null, 0));

        var put = await SendAsync(HttpMethod.Put, Url(student), directeur,
            new { exemptions = new[] { new { subjectId = subject, reason = "Inaptitude médicale" } } });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Le Secrétariat relit le motif, puis retire la dispense (liste vide).
        var secretaire = await SecretaireAsync();
        var after = await (await SendAsync(HttpMethod.Get, Url(student), secretaire)).Content.ReadFromJsonAsync<ExemptionsDto>();
        after!.Subjects.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ExemptibleSubject(subject, "Éducation physique", true, "Inaptitude médicale", 0));

        (await SendAsync(HttpMethod.Put, Url(student), secretaire, new { exemptions = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cleared = await (await SendAsync(HttpMethod.Get, Url(student), secretaire)).Content.ReadFromJsonAsync<ExemptionsDto>();
        cleared!.Subjects.Should().OnlyContain(s => !s.IsExempt);
    }

    [Fact]
    public async Task A_Missing_Reason_Is_A_422_And_An_Unknown_Student_A_404()
    {
        var directeur = await DirecteurAsync();
        var (student, subject) = await SeedAsync(directeur);

        var noReason = await SendAsync(HttpMethod.Put, Url(student), directeur,
            new { exemptions = new[] { new { subjectId = subject, reason = "  " } } });
        noReason.StatusCode.Should().Be((HttpStatusCode)422);

        (await SendAsync(HttpMethod.Get, Url(Guid.NewGuid()), directeur)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, Url(Guid.NewGuid()), directeur, new { exemptions = Array.Empty<object>() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
