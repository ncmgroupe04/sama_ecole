using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Enrollments;

/// <summary>
/// Ticket JGK-E01 — inscriptions, de bout en bout contre un vrai PostgreSQL.
///
/// Le critère central du ticket : le service Finance ne peut PAS composer un montant dû. On le
/// prouve par le test d'autorisation négatif obligatoire (Finance → 403 sur POST /enrollments).
/// Le chemin nominal (Secrétariat → 201, matricule généré, montant calculé, reçu relisible) vérifie
/// que l'inscription relie bien année active, classe, matricule et barème.
/// </summary>
public class EnrollmentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public EnrollmentsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9; // aucune ligne de réglages semée → défaut.
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record ReceiptLine(string Designation, bool IsRecurring, decimal UnitAmount, int Months, decimal LineTotal);
    private record Receipt(
        Guid EnrollmentId, string SchoolName, string? SchoolPhone, string Matricule, string StudentFullName,
        string ClassroomName, string ClassroomLevel, string SchoolYearLabel, string Type, string Status,
        DateTimeOffset EnrolledAt, List<ReceiptLine> Lines, decimal TotalDue);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() =>
        TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() =>
        TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    /// <summary>Prépare une école prête à inscrire : une classe, une année active, un barème complet.</summary>
    private async Task<Guid> SeedEnrollableSchoolAsync(string directeurToken)
    {
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeurToken,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        // Première année de l'école → active d'office (JGK-C01). Dates relatives : l'année reste « en cours ».
        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeurToken, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await SeedFeeAsync(directeurToken, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeurToken, "Mensualité", isRecurring: true, amount: Mensualite);

        return classroom.Id;
    }

    private async Task SeedFeeAsync(string token, string name, bool isRecurring, decimal amount)
    {
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static object NewEnrollmentBody(Guid classroomId, string fullName) => new
    {
        type = "NewEnrollment",
        classroomId,
        fullName,
        birthDate = "2015-05-20",
        gender = "F"
    };

    [Fact]
    public async Task Finance_Must_Not_Be_Allowed_To_Create_An_Enrollment()
    {
        // Critère du ticket : le service Finance ne compose jamais un montant dû (règle #4).
        // Le contrôle de rôle précède tout traitement — inutile même de semer le barème.
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", finance,
            NewEnrollmentBody(Guid.NewGuid(), "Tentative Interdite"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Secretariat_Creates_An_Enrollment_With_The_Computed_Total_And_A_Readable_Receipt()
    {
        var directeur = await DirecteurTokenAsync();
        var classroomId = await SeedEnrollableSchoolAsync(directeur);

        var secretaire = await SecretaireTokenAsync();
        var createResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroomId, "Awa Ndiaye"));

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await createResponse.Content.ReadFromJsonAsync<Receipt>())!;

        receipt.Matricule.Should().NotBeNullOrWhiteSpace("le matricule est généré à l'enregistrement");
        receipt.StudentFullName.Should().Be("Awa Ndiaye");
        receipt.Type.Should().Be("NewEnrollment");
        receipt.TotalDue.Should().Be(ExpectedTotal, "frais ponctuel + mensualité × 9 mois (réglage par défaut)");
        receipt.Lines.Should().HaveCount(2);
        receipt.Lines.Single(l => l.IsRecurring).Months.Should().Be(DefaultTuitionMonths);

        // Le reçu se relit à l'identique (réimpression) — même montant, mêmes lignes figées.
        var receiptResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/enrollments/{receipt.EnrollmentId}/receipt", secretaire);
        receiptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reread = (await receiptResponse.Content.ReadFromJsonAsync<Receipt>())!;

        reread.TotalDue.Should().Be(ExpectedTotal);
        reread.Matricule.Should().Be(receipt.Matricule);
        reread.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Enrolling_Without_An_Active_Year_Is_Rejected()
    {
        // Classe et barème existent, mais aucune année scolaire active : rien à rattacher → 422.
        var directeur = await DirecteurTokenAsync();
        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CI", level = "Primaire", capacity = 40 });
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var secretaire = await SecretaireTokenAsync();
        var response = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire,
            NewEnrollmentBody(classroom.Id, "Sans Année"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Creating_An_Enrollment_Without_A_Token_Should_Return_401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/enrollments",
            NewEnrollmentBody(Guid.NewGuid(), "Anonyme"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
