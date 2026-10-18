using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Students;

/// <summary>
/// Suppression guidée d'un élève, de bout en bout contre un vrai PostgreSQL (spec 2026-10-17) : la suppression directe
/// est refusée en énumérant les liens, la rupture est une action explicite, un paiement actif bloque sans option de
/// rupture, et l'annulation du paiement rend l'élève supprimable.
/// </summary>
public class StudentDeletionEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public StudentDeletionEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name);
    private record FeeCategoryDto(Guid Id, string Name);
    private record Receipt(Guid EnrollmentId, string ReceiptNumber, string Matricule, decimal TotalDue);
    private record PaymentResult(Guid PaymentId, string ReceiptNumber, decimal Amount);
    private record StudentListItem(Guid Id, string Matricule, string FullName);
    private record PaginatedStudents(List<StudentListItem> Items, int TotalCount);
    private record StudentIdentity(Guid Id, uint RowVersion);
    private record StudentDetail(StudentIdentity Identity);
    private record Link(string Kind, string Label, int Count, bool Breakable, string BreakAction);
    private record Impact(bool CanDelete, bool FinancialBlock, string? Message, List<Link> Links);
    private record ApiError(string Code, string Message);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task SeedFeeAsync(string token, string name, bool isRecurring, decimal amount)
    {
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token, new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>École prête + un élève inscrit (sans aucun paiement).</summary>
    private async Task<Receipt> SeedEnrolledStudentAsync()
    {
        var directeur = await DirecteurTokenAsync();

        var classroomResponse = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", directeur,
            new { name = "CM2", level = "Primaire", capacity = 40 });
        classroomResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var classroom = (await classroomResponse.Content.ReadFromJsonAsync<ClassroomDto>())!;

        var yearResponse = await SendAsync(HttpMethod.Post, "/api/v1/school-years", directeur, new
        {
            label = $"{Today.Year}-{Today.Year + 1}",
            startDate = Today.AddDays(-150).ToString("yyyy-MM-dd"),
            endDate = Today.AddDays(+120).ToString("yyyy-MM-dd")
        });
        yearResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await SeedFeeAsync(directeur, "Inscription", isRecurring: false, amount: Inscription);
        await SeedFeeAsync(directeur, "Mensualité", isRecurring: true, amount: Mensualite);

        var secretaire = await SecretaireTokenAsync();
        var enrollResponse = await SendAsync(HttpMethod.Post, "/api/v1/enrollments", secretaire, new
        {
            type = "NewEnrollment",
            classroomId = classroom.Id,
            fullName = "Awa Ndiaye",
            birthDate = "2015-05-20",
            birthPlace = "Dakar",
            gender = "F"
        });
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await enrollResponse.Content.ReadFromJsonAsync<Receipt>())!;
    }

    private async Task<Guid> ResolveStudentIdAsync(string token, string matricule)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students?search={matricule}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedStudents>())!;
        return page.Items.Single(s => s.Matricule == matricule).Id;
    }

    private async Task<(Guid Id, uint RowVersion)> DetailAsync(string token, string matricule)
    {
        var id = await ResolveStudentIdAsync(token, matricule);
        var detail = (await (await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}", token))
            .Content.ReadFromJsonAsync<StudentDetail>())!;
        return (id, detail.Identity.RowVersion);
    }

    [Fact]
    public async Task An_Enrolled_Student_Without_Payment_Is_Blocked_Then_Unlinked_Then_Deleted()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var secretaire = await SecretaireTokenAsync();
        var (id, rowVersion) = await DetailAsync(secretaire, enrollment.Matricule);

        // 1) La suppression directe est refusée et énumère les liens.
        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", secretaire);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("DEPENDENCIES_EXIST");
        error.Message.Should().Contain("1 inscription").And.Contain("Vous devez rompre ces liens");

        // 2) L'analyse dit la même chose, sans rien modifier.
        var impact = (await (await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}/deletion-impact", secretaire))
            .Content.ReadFromJsonAsync<Impact>())!;
        impact.CanDelete.Should().BeFalse();
        impact.Links.Should().Contain(l => l.Kind == "Enrollment" && l.Breakable);

        // 3) Rupture explicite, puis suppression.
        var unlink = await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", secretaire);
        unlink.StatusCode.Should().Be(HttpStatusCode.OK);
        (await unlink.Content.ReadFromJsonAsync<Impact>())!.CanDelete.Should().BeTrue();

        var deleted = await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", secretaire);
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4) L'élève a disparu des listes.
        var list = await SendAsync(HttpMethod.Get, $"/api/v1/students?search={enrollment.Matricule}", secretaire);
        (await list.Content.ReadFromJsonAsync<PaginatedStudents>())!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Student_With_A_Payment_Cannot_Be_Deleted_Until_The_Payment_Is_Cancelled()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        (await SendAsync(HttpMethod.Post, "/api/v1/finance/sessions/open", finance, new { openingBalance = 0m }))
            .EnsureSuccessStatusCode();
        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = enrollment.EnrollmentId, amount = 20_000m, method = "Cash", category = "Tuition" });
        pay.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await pay.Content.ReadFromJsonAsync<PaymentResult>())!;

        var directeur = await DirecteurTokenAsync();
        var (id, rowVersion) = await DetailAsync(directeur, enrollment.Matricule);

        // Bloqué, sans option de rupture, avec le texte imposé.
        var blocked = await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", directeur);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = (await blocked.Content.ReadFromJsonAsync<ApiError>())!;
        error.Code.Should().Be("FINANCIAL_RECORDS_EXIST");
        error.Message.Should().Be("Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.");
        (await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Option A : on annule le paiement, l'élève redevient supprimable.
        (await SendAsync(HttpMethod.Post, $"/api/v1/finance/payments/{payment.PaymentId}/cancel", directeur,
            new { reason = "Saisi sur le mauvais élève" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"/api/v1/students/{id}?rowVersion={rowVersion}", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Finance_Cannot_Use_The_Deletion_Routes()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        var id = await ResolveStudentIdAsync(finance, enrollment.Matricule);

        (await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}/deletion-impact", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/v1/students/{id}/unlink-dependencies", finance)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Deletion_Routes_Return_404_For_An_Unknown_Student()
    {
        var directeur = await DirecteurTokenAsync();

        (await SendAsync(HttpMethod.Get, $"/api/v1/students/{Guid.NewGuid()}/deletion-impact", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/v1/students/{Guid.NewGuid()}/unlink-dependencies", directeur))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
