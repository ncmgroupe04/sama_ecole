using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Ticket JGK-F04 — tableau de bord financier, de bout en bout contre un vrai PostgreSQL.
///
/// Deux critères du ticket :
///   • séparation des rôles (règle #4) — Directeur et Finance voient le tableau de bord, le
///     Secrétariat NON (il compose le dû à l'inscription, il ne pilote pas la trésorerie) ;
///   • les agrégats (encaissé jour/mois/année, solde dû, taux de recouvrement) reflètent RÉELLEMENT
///     les paiements enregistrés via la caisse, jamais une valeur inventée.
/// </summary>
public class FinanceDashboardEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public FinanceDashboardEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths; // 145 000

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string ReceiptNumber, string Matricule, decimal TotalDue);
    private record PaymentResult(Guid PaymentId, string ReceiptNumber, decimal Amount);
    private record RecentPayment(Guid PaymentId, string ReceiptNumber, string Matricule, string StudentFullName, decimal Amount, string Method, DateTimeOffset PaidAt);
    private record DashboardDto(
        decimal CollectedToday, decimal CollectedThisMonth, decimal CollectedThisYear,
        decimal OutstandingBalance, decimal RecoveryRate, List<RecentPayment> RecentPayments);

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
        var categoryResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fee-categories", token,
            new { name, isRecurring });
        categoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var category = (await categoryResponse.Content.ReadFromJsonAsync<FeeCategoryDto>())!;

        var applyResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/fees/apply-standard", token,
            new { feeCategoryId = category.Id, amount, overwriteExisting = false });
        applyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>École prête + un élève inscrit : renvoie l'inscription à encaisser (Id + montant dû).</summary>
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
            gender = "F"
        });
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await enrollResponse.Content.ReadFromJsonAsync<Receipt>())!;
        receipt.TotalDue.Should().Be(ExpectedTotal);
        return receipt;
    }

    [Fact]
    public async Task Dashboard_Reflects_Payments_Just_Recorded_Through_The_Caisse()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();

        var first = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = enrollment.EnrollmentId, amount = 50_000m, method = "Cash" });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = enrollment.EnrollmentId, amount = 30_000m, method = "MobileMoney" });
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        const decimal totalCollected = 80_000m;

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/dashboard", finance);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DashboardDto>())!;

        // Les deux versements ont lieu "maintenant" : ils tombent forcément dans les trois fenêtres.
        dashboard.CollectedToday.Should().Be(totalCollected);
        dashboard.CollectedThisMonth.Should().Be(totalCollected);
        dashboard.CollectedThisYear.Should().Be(totalCollected);
        dashboard.OutstandingBalance.Should().Be(ExpectedTotal - totalCollected);
        dashboard.RecoveryRate.Should().Be(totalCollected / ExpectedTotal);

        dashboard.RecentPayments.Should().HaveCount(2);
        dashboard.RecentPayments.Select(p => p.Amount).Should().Contain([50_000m, 30_000m]);
        dashboard.RecentPayments.Should().OnlyContain(p => p.Matricule == enrollment.Matricule);
        // Le plus récent d'abord : le second versement (MobileMoney, 30 000) a été encaissé après le premier.
        dashboard.RecentPayments[0].Method.Should().Be("MobileMoney");
    }

    [Fact]
    public async Task Dashboard_Shows_Zero_When_Nothing_Is_Due_Or_Collected()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/dashboard", directeur);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dashboard = (await response.Content.ReadFromJsonAsync<DashboardDto>())!;

        dashboard.CollectedToday.Should().Be(0m);
        dashboard.CollectedThisMonth.Should().Be(0m);
        dashboard.CollectedThisYear.Should().Be(0m);
        dashboard.OutstandingBalance.Should().Be(0m);
        dashboard.RecoveryRate.Should().Be(0m, "aucune inscription active ne doit produire une division par zéro");
        dashboard.RecentPayments.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_View_The_Dashboard()
    {
        // Miroir du critère JGK-F02 : la Finance/le Directeur pilotent la trésorerie, le Secrétariat
        // compose le dû à l'inscription mais n'a pas accès à la vue agrégée (règle #4).
        var secretaire = await SecretaireTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/dashboard", secretaire);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
