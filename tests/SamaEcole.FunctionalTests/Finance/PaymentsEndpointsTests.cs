using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Finance;

/// <summary>
/// Ticket JGK-F02 — caisse, de bout en bout contre un vrai PostgreSQL.
///
/// Deux critères du ticket :
///   • séparation des rôles (règle #4) — ce que la règle sépare est FIXER LE DÛ (Secrétariat/Directeur,
///     jamais Finance) de la santé financière AGRÉGÉE (Directeur/Finance) ; ENCAISSER, lui, est ouvert
///     au Secrétariat qui tient sa propre caisse (cf. Volume 7). On le prouve par le test positif
///     Secrétariat + session ouverte → 201 sur POST /finance/payments ;
///   • on n'encaisse jamais au-delà du solde, et un reçu officiel sort du pipeline.
/// (La non-régression sous concurrence est prouvée à part par PaymentConcurrencyTests, en intégration.)
/// </summary>
public class PaymentsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PaymentsEndpointsTests(AuthApiFactory factory)
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
    private record PaymentResult(
        Guid PaymentId, string ReceiptNumber, decimal Amount, decimal TotalDue,
        decimal AmountPaid, decimal RemainingBalance, string Status);
    private record StudentListItem(Guid Id, string Matricule, string FullName);
    private record PaginatedStudents(List<StudentListItem> Items, int TotalCount);
    private record StudentBalance(
        Guid EnrollmentId, Guid StudentId, string Matricule, string StudentFullName,
        string ClassroomName, string SchoolYearLabel,
        decimal TotalDue, decimal AmountPaid, decimal RemainingBalance, string Status,
        List<Installment> Installments, decimal DueNowTotal);

    private record Installment(
        string Id, string Designation, decimal Amount, decimal AmountPaid, decimal RemainingDue,
        DateTimeOffset DueDate, string Status, bool IsInitialScope, Guid? FeeCategoryId);

    private record CashierSearchResult(
        Guid Id, string Matricule, string FullName, string? ClassroomName,
        bool HasActiveEnrollment, decimal TotalDue, decimal AmountPaid,
        decimal RemainingBalance, decimal DueNowTotal);

    private record PaymentReceiptLine(string Designation, string? Label, decimal Amount);
    private record PaymentReceipt(
        string ReceiptNumber, decimal Amount, decimal TotalDue, decimal AlreadyPaid,
        decimal RemainingBalance, List<PaymentReceiptLine> Lines, bool HasBalancedLines);

    // Engagement initial de SeedEnrolledStudentAsync : inscription (ponctuel) + 1er mois de scolarité.
    private const decimal ExpectedDueNow = Inscription + Mensualite; // 25 000

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> EnseignantTokenAsync() => TokenAsync(AuthApiFactory.EnseignantEmail, AuthApiFactory.EnseignantPassword);

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
            birthPlace = "Dakar",
            gender = "F"
        });
        enrollResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var receipt = (await enrollResponse.Content.ReadFromJsonAsync<Receipt>())!;
        receipt.TotalDue.Should().Be(ExpectedTotal);
        return receipt;
    }

    private static object PaymentBody(Guid enrollmentId, decimal amount, string method = "Cash", string category = "Tuition") =>
        new { enrollmentId, amount, method, category };

    private async Task OpenFinanceSessionAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/sessions/open", token, new { openingBalance = 0m });
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Résout un élève par matricule, comme le fera la recherche de l'écran caisse.</summary>
    private async Task<Guid> ResolveStudentIdAsync(string token, string matricule)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students?search={matricule}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PaginatedStudents>())!;
        return page.Items.Single(s => s.Matricule == matricule).Id;
    }

    [Fact]
    public async Task The_Student_Balance_Endpoint_Reflects_The_Active_Enrollment()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);
        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);

        var before = await SendAsync(HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance);
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceBefore = (await before.Content.ReadFromJsonAsync<StudentBalance>())!;
        balanceBefore.EnrollmentId.Should().Be(enrollment.EnrollmentId);
        balanceBefore.TotalDue.Should().Be(ExpectedTotal);
        balanceBefore.AmountPaid.Should().Be(0m);
        balanceBefore.RemainingBalance.Should().Be(ExpectedTotal);
        balanceBefore.Status.Should().Be("Partial");

        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 60_000m));
        pay.StatusCode.Should().Be(HttpStatusCode.Created);

        var after = await SendAsync(HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance);
        var balanceAfter = (await after.Content.ReadFromJsonAsync<StudentBalance>())!;
        balanceAfter.AmountPaid.Should().Be(60_000m);
        balanceAfter.RemainingBalance.Should().Be(ExpectedTotal - 60_000m);
    }

    [Fact]
    public async Task The_Balance_Of_An_Unknown_Student_Returns_404()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{Guid.NewGuid()}/balance", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Finance_Records_A_Partial_Payment_And_Gets_A_Receipt_Number_And_Balance()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 50_000m, "MobileMoney"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<PaymentResult>())!;

        result.ReceiptNumber.Should().MatchRegex(@"^REC-\d{4}-\d{4}$", "le reçu officiel est numéroté comme celui d'inscription");
        result.Amount.Should().Be(50_000m);
        result.RemainingBalance.Should().Be(ExpectedTotal - 50_000m);
        result.Status.Should().Be("Partial", "il reste un solde après ce versement");
    }

    [Fact]
    public async Task Paying_The_Full_Balance_Settles_It_And_A_Further_Payment_Is_Refused()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var full = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, ExpectedTotal));
        full.StatusCode.Should().Be(HttpStatusCode.Created);
        var settled = (await full.Content.ReadFromJsonAsync<PaymentResult>())!;
        settled.RemainingBalance.Should().Be(0m);
        settled.Status.Should().Be("Paid", "le solde est nul après le versement intégral");

        // Une inscription déjà soldée n'accepte plus d'encaissement.
        var again = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 1_000m));
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Payment_Larger_Than_The_Remaining_Balance_Is_Refused()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, ExpectedTotal + 1m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Secretariat_With_An_Open_Session_Can_Record_A_Payment()
    {
        // Le Secrétariat tient sa propre caisse (Volume 7) : session ouverte à son nom, il encaisse
        // comme la Finance. La règle #4 lui interdit la santé financière agrégée, pas l'encaissement.
        var enrollment = await SeedEnrolledStudentAsync();

        var secretaire = await SecretaireTokenAsync();
        await OpenFinanceSessionAsync(secretaire);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", secretaire,
            PaymentBody(enrollment.EnrollmentId, 30_000m));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await response.Content.ReadFromJsonAsync<PaymentResult>())!;
        result.ReceiptNumber.Should().MatchRegex(@"^REC-\d{4}-\d{4}$");
        result.Amount.Should().Be(30_000m);
    }

    [Fact]
    public async Task Recording_A_Payment_Without_Any_Open_Session_Is_Refused()
    {
        // Invariant de caisse (Volume 1 §14) : pas de session ouverte pour l'opérateur ⇒ 422, quel que
        // soit le rôle. Vérifié ici pour la Finance, hors de tout OpenFinanceSessionAsync.
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 5_000m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_Payment_Receipt_Can_Be_Downloaded_As_A_Pdf()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var payResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 25_000m));
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await payResponse.Content.ReadFromJsonAsync<PaymentResult>())!;

        var pdfResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/payments/{result.PaymentId}/receipt/pdf", finance);

        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        pdfResponse.Content.Headers.ContentDisposition!.FileName.Should().Contain(result.ReceiptNumber);

        var bytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "l'en-tête magique d'un PDF");
    }

    [Fact]
    public async Task Listing_Payments_Is_Reserved_To_Directeur_And_Finance()
    {
        // GET /finance/payments expose montants, méthodes et élèves pour toute l'école — pas un reçu
        // individuel où le tenant vient du JWT. Un Enseignant ne doit pas pouvoir la parcourir.
        var enseignant = await EnseignantTokenAsync();

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/payments", enseignant);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Finance_Can_List_Payments()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var pay = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(enrollment.EnrollmentId, 30_000m));
        pay.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/finance/payments", finance);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Paying_An_Unknown_Enrollment_Returns_404()
    {
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            PaymentBody(Guid.NewGuid(), 5_000m));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cashier_Search_And_Balance_Expose_The_Due_Now_Total_Not_The_Annual_Total()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        // La recherche caisse : le badge affiche le MONTANT ÉCHU (inscription + 1er mois), jamais le
        // cumul annuel.
        var searchResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/search?q={enrollment.Matricule}", finance);
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var hits = (await searchResponse.Content.ReadFromJsonAsync<List<CashierSearchResult>>())!;
        var hit = hits.Single(h => h.Matricule == enrollment.Matricule);
        hit.TotalDue.Should().Be(ExpectedTotal);        // le cumul annuel existe toujours…
        hit.DueNowTotal.Should().Be(ExpectedDueNow);    // …mais ce n'est pas ce que le badge montre

        // Le solde détaillé porte le même montant échu et marque les échéances de l'engagement initial.
        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);
        var balanceResponse = await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance);
        var balance = (await balanceResponse.Content.ReadFromJsonAsync<StudentBalance>())!;
        balance.DueNowTotal.Should().Be(ExpectedDueNow);

        var initialScope = balance.Installments.Where(i => i.IsInitialScope).ToList();
        initialScope.Select(i => i.Designation).Should().BeEquivalentTo(["Inscription", "Mensualité (Mois 1)"]);
        initialScope.Should().OnlyContain(i => i.FeeCategoryId != null);
        balance.Installments.Where(i => !i.IsInitialScope)
            .Should().OnlyContain(i => i.Designation.StartsWith("Mensualité (Mois "));
    }

    [Fact]
    public async Task Settling_The_Due_Fees_In_One_Payment_Ventilates_The_Receipt_And_Marks_Each_Line_Paid()
    {
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();
        await OpenFinanceSessionAsync(finance);

        var studentId = await ResolveStudentIdAsync(finance, enrollment.Matricule);
        var balance = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance))
            .Content.ReadFromJsonAsync<StudentBalance>())!;

        // Un SEUL versement pour toutes les échéances dues, ventilé poste par poste (le guichet rapide
        // envoie exactement cela).
        var due = balance.Installments.Where(i => i.IsInitialScope).ToList();
        var breakdowns = due.Select(i => new
        {
            feeCategoryId = i.FeeCategoryId,
            amountAllocated = i.RemainingDue,
            label = i.Designation.Contains("(Mois 1)") ? "Mois 1" : (string?)null
        }).ToArray();

        var payResponse = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance, new
        {
            enrollmentId = enrollment.EnrollmentId,
            amount = ExpectedDueNow,
            method = "Cash",
            breakdowns
        });
        payResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await payResponse.Content.ReadFromJsonAsync<PaymentResult>())!;

        // UN reçu, deux lignes qui totalisent exactement l'encaissement.
        var receipt = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/payments/{result.PaymentId}/receipt", finance))
            .Content.ReadFromJsonAsync<PaymentReceipt>())!;
        receipt.Amount.Should().Be(ExpectedDueNow);
        receipt.HasBalancedLines.Should().BeTrue();
        receipt.Lines.Should().HaveCount(2);
        receipt.Lines.Sum(l => l.Amount).Should().Be(ExpectedDueNow);
        receipt.Lines.Select(l => l.Designation).Should().BeEquivalentTo(["Inscription", "Mensualité"]);
        receipt.Lines.Single(l => l.Designation == "Mensualité").Label.Should().Be("Mois 1");

        // Les postes réglés passent individuellement à « Réglé » ; le mois 2 reste en attente.
        var after = (await (await SendAsync(
            HttpMethod.Get, $"/api/v1/finance/students/{studentId}/balance", finance))
            .Content.ReadFromJsonAsync<StudentBalance>())!;
        after.DueNowTotal.Should().Be(0m);
        after.Installments.Single(i => i.Designation == "Inscription").Status.Should().Be("Paid");
        after.Installments.Single(i => i.Designation == "Mensualité (Mois 1)").Status.Should().Be("Paid");
        // Le mois suivant n'est pas touché — encore dû (échu ou à venir selon la date), jamais « Réglé ».
        var monthTwo = after.Installments.Single(i => i.Designation == "Mensualité (Mois 2)");
        monthTwo.Status.Should().BeOneOf("Pending", "Overdue");
        monthTwo.RemainingDue.Should().Be(Mensualite);
    }
}
