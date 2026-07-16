using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.AuditLogs;

/// <summary>
/// Ticket JGK-H01 — journal d'audit centralisé, de bout en bout contre un vrai PostgreSQL.
///
/// Critère du ticket : « chaque écriture sensible (JGK-F02, JGK-G01, JGK-A05) produit une entrée
/// d'audit correspondante — vérifié par test d'intégration croisé. » JGK-G01 (notes) n'existe pas
/// encore ; ce fichier couvre les deux tickets déjà construits (F02, A05) plus les extensions
/// naturelles du même journal (mot de passe, création de compte/établissement).
/// </summary>
public class AuditLogsEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuditLogsEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;
    private const int DefaultTuitionMonths = 9;
    private const decimal ExpectedTotal = Inscription + Mensualite * DefaultTuitionMonths;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record FeeCategoryDto(Guid Id, string Name, bool IsRecurring);
    private record Receipt(Guid EnrollmentId, string ReceiptNumber, string Matricule, decimal TotalDue);
    private record AuditLogEntry(
        Guid Id, string ActorFullName, string Module, string Action, bool Success,
        string? FailureReason, string? IpAddress, DateTimeOffset OccurredAt);
    private record PaginatedAuditLogs(List<AuditLogEntry> Items, int TotalCount, int Page, int PageSize);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SecretaireTokenAsync() => TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
    private Task<string> FinanceTokenAsync() => TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);
    private Task<string> SuperAdminTokenAsync() => TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<PaginatedAuditLogs> GetAuditLogsAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/audit-logs", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PaginatedAuditLogs>())!;
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
    public async Task Recording_A_Payment_Should_Produce_A_Corresponding_Audit_Entry()
    {
        // Critère explicite du ticket : JGK-F02 (paiements) doit produire une entrée d'audit.
        var enrollment = await SeedEnrolledStudentAsync();
        var finance = await FinanceTokenAsync();

        var payment = await SendAsync(HttpMethod.Post, "/api/v1/finance/payments", finance,
            new { enrollmentId = enrollment.EnrollmentId, amount = 50_000m, method = "Cash" });
        payment.StatusCode.Should().Be(HttpStatusCode.Created);

        var directeur = await DirecteurTokenAsync();
        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().ContainSingle(l => l.Module == "Finance" && l.Action == "RecordPayment" && l.Success);
    }

    [Fact]
    public async Task Changing_A_User_Status_Should_Produce_A_Corresponding_Audit_Entry()
    {
        // Critère explicite du ticket : JGK-A05 (changement de statut) doit produire une entrée d'audit.
        var directeur = await DirecteurTokenAsync();

        var change = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{AuthApiFactory.SecretaireId}/status", directeur,
            new { status = "Suspended", reason = "Absences répétées non justifiées" });
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().ContainSingle(l =>
            l.Module == "Users" && l.Action == "ChangeUserStatus" && l.Success
            && l.ActorFullName == "Directeur de test");
    }

    [Fact]
    public async Task A_Failed_Attempt_Should_Produce_A_Failure_Entry_With_A_Reason()
    {
        var directeur = await DirecteurTokenAsync();

        // Un Directeur ne peut pas changer son propre statut (JGK-A05) : tentative rejetée, mais
        // journalisée quand même — c'est précisément l'objet du champ « résultat » du journal
        // (docs/Volume_7_Security.md §7).
        var change = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{AuthApiFactory.DirecteurId}/status", directeur,
            new { status = "Blocked", reason = "Tentative invalide" });
        change.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var logs = await GetAuditLogsAsync(directeur);

        var failure = logs.Items.Should().ContainSingle(l => l.Module == "Users" && l.Action == "ChangeUserStatus" && !l.Success).Subject;
        failure.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Resetting_A_Password_Should_Produce_A_Corresponding_Audit_Entry()
    {
        var directeur = await DirecteurTokenAsync();

        var reset = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{AuthApiFactory.SecretaireId}/password", directeur,
            new { newPassword = "Correct-Horse-Battery-9!" });
        reset.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().ContainSingle(l => l.Module == "Users" && l.Action == "ResetUserPassword" && l.Success);
    }

    [Fact]
    public async Task Creating_A_User_Should_Produce_A_Corresponding_Audit_Entry()
    {
        var directeur = await DirecteurTokenAsync();

        var create = await SendAsync(HttpMethod.Post, "/api/v1/users", directeur, new
        {
            fullName = "Nouvelle Recrue",
            email = "nouvelle.recrue@sama-ecole.sn",
            password = "Correct-Horse-Battery-9!",
            role = "Enseignant"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().ContainSingle(l => l.Module == "Users" && l.Action == "CreateUser" && l.Success);
    }

    [Fact]
    public async Task Creating_A_School_Should_Not_Crash_Even_Though_Its_Audit_Entry_Is_Not_Yet_Captured()
    {
        // CreateSchoolCommand n'est délibérément PAS IAuditableRequest (voir la classe CreateSchoolCommand
        // et AuditLoggingBehavior) : le Super Admin n'a aucun SchoolId propre, et la policy RLS de
        // audit_logs rejetterait tout INSERT depuis sa session — capturer « actions Super Admin » dans
        // ce journal exigerait sa propre fonction SECURITY DEFINER, comme provision_school_director.
        // Ce test garde la trace du choix : la création d'école doit rester 201, jamais un 500.
        var superAdmin = await SuperAdminTokenAsync();

        var createSchool = await SendAsync(HttpMethod.Post, "/api/v1/schools", superAdmin, new
        {
            name = "École Les Filaos",
            address = "Rue 12, Médina, Dakar",
            phone = "+221771234567",
            directorEmail = "directrice@filaos.sn",
            directorFullName = "Aminata Sow"
        });

        createSchool.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(nameof(SecretaireTokenAsync))]
    [InlineData(nameof(FinanceTokenAsync))]
    public async Task Only_A_Directeur_May_Read_The_Audit_Log(string tokenFactoryName)
    {
        var token = tokenFactoryName switch
        {
            nameof(SecretaireTokenAsync) => await SecretaireTokenAsync(),
            nameof(FinanceTokenAsync) => await FinanceTokenAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(tokenFactoryName))
        };

        var response = await SendAsync(HttpMethod.Get, "/api/v1/audit-logs", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_Log_Should_Be_Empty_When_Nothing_Sensitive_Has_Happened_Yet()
    {
        var directeur = await DirecteurTokenAsync();

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().BeEmpty();
        logs.TotalCount.Should().Be(0);
    }
}
