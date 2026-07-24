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
            birthPlace = "Dakar",
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

        await SendAsync(HttpMethod.Post, "/api/v1/finance/sessions/open", finance, new { openingBalance = 0m });

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
    public async Task Creating_A_School_Should_Produce_An_Audit_Entry_Attributed_To_The_New_School()
    {
        // CreateSchoolCommand n'est délibérément PAS IAuditableRequest (voir AuditLoggingBehavior) :
        // le Super Admin n'a aucun SchoolId propre, donc le mécanisme générique ne trouverait rien à
        // qui imputer l'entrée. Le Handler écrit lui-même via IAuditLogStore une fois l'école connue
        // (même contournement RLS que Login) — ce test vérifie que l'entrée existe vraiment, lue
        // depuis le journal de l'école NOUVELLEMENT créée (le Super Admin, lui, ne peut pas lire
        // /api/v1/audit-logs : il n'a pas de SchoolId).
        _factory.Emails.Clear();
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

        var email = _factory.Emails.LastTo("directrice@filaos.sn");
        email.Should().NotBeNull();
        var password = FakeEmailSender.ExtractPassword(email!);

        var directrice = await TokenAsync("directrice@filaos.sn", password);
        var logs = await GetAuditLogsAsync(directrice);

        logs.Items.Should().ContainSingle(l => l.Module == "Schools" && l.Action == "CreateSchool" && l.Success);
    }

    private async Task LoginExpectingUnauthorizedAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Chaque test ci-dessous récupère le token du Directeur AVANT l'action sous test, puis le
    // réutilise pour lire le journal : se reconnecter APRÈS produirait sa propre entrée « Auth/Login »
    // et fausserait les assertions d'unicité/vacuité (la lecture du journal exige un Directeur connecté,
    // et cette connexion est elle-même désormais journalisée).

    [Fact]
    public async Task A_Successful_Login_Should_Produce_A_Corresponding_Audit_Entry()
    {
        var directeur = await DirecteurTokenAsync();

        await TokenAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().ContainSingle(l =>
            l.Module == "Auth" && l.Action == "Login" && l.Success && l.ActorFullName == "Secrétaire de test");
    }

    [Fact]
    public async Task A_Wrong_Password_Should_Produce_A_Failure_Entry_With_A_Reason()
    {
        var directeur = await DirecteurTokenAsync();

        await LoginExpectingUnauthorizedAsync(AuthApiFactory.SecretaireEmail, "mauvais-mot-de-passe");

        var logs = await GetAuditLogsAsync(directeur);

        var failure = logs.Items.Should()
            .ContainSingle(l => l.Module == "Auth" && l.Action == "Login" && !l.Success).Subject;
        failure.FailureReason.Should().Be("Mot de passe invalide.");
    }

    [Fact]
    public async Task Login_Attempts_Past_The_Lockout_Threshold_Should_Produce_A_Lockout_Entry()
    {
        var directeur = await DirecteurTokenAsync();

        // AuthApiFactory fixe Auth__MaxFailedAttempts=5 : la 5e tentative déclenche le verrou, la 6e
        // tombe donc dans la branche « déjà verrouillé » du Handler.
        for (var i = 0; i < 5; i++)
        {
            await LoginExpectingUnauthorizedAsync(AuthApiFactory.SecretaireEmail, "mauvais-mot-de-passe");
        }
        await LoginExpectingUnauthorizedAsync(AuthApiFactory.SecretaireEmail, "mauvais-mot-de-passe");

        var logs = await GetAuditLogsAsync(directeur);

        var lockout = logs.Items.Should()
            .ContainSingle(l => l.Module == "Auth" && l.Action == "Login" && !l.Success
                && l.FailureReason != null && l.FailureReason.Contains("verrouillé")).Subject;
        lockout.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_On_A_Suspended_Account_Should_Produce_A_Failure_Entry_With_The_Status()
    {
        var directeur = await DirecteurTokenAsync();
        var suspend = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{AuthApiFactory.SecretaireId}/status", directeur,
            new { status = "Suspended", reason = "Absences répétées non justifiées" });
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        await LoginExpectingUnauthorizedAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);

        var logs = await GetAuditLogsAsync(directeur);

        var failure = logs.Items.Should()
            .ContainSingle(l => l.Module == "Auth" && l.Action == "Login" && !l.Success
                && l.FailureReason != null && l.FailureReason.Contains("statut")).Subject;
        failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_With_Unknown_Email_Should_Not_Produce_Any_Audit_Entry()
    {
        var directeur = await DirecteurTokenAsync();

        // Aucun utilisateur ni école à qui imputer l'entrée (voir la remarque de classe de
        // LoginCommandHandler) : volontairement pas journalisé dans cette table tenant.
        await LoginExpectingUnauthorizedAsync("inconnu@sama-ecole.sn", "peu-importe");

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().NotContain(l => l.Module == "Auth" && l.Action == "Login" && l.ActorFullName != "Directeur de test");
    }

    [Fact]
    public async Task SuperAdmin_Login_Should_Not_Produce_Any_Tenant_Audit_Entry()
    {
        var directeur = await DirecteurTokenAsync();

        // Le Super Admin n'a aucun SchoolId propre (relève de PlatformAuditLogs, hors périmètre MVP).
        await TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().NotContain(l => l.Module == "Auth" && l.Action == "Login" && l.ActorFullName != "Directeur de test");
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
    public async Task The_Log_Should_Contain_Only_The_Directeurs_Own_Login_When_Nothing_Else_Has_Happened()
    {
        // Se connecter pour LIRE le journal est lui-même désormais un événement journalisé (JGK-A04) :
        // la seule entrée « rien de sensible ne s'est encore produit » est donc celle-là.
        var directeur = await DirecteurTokenAsync();

        var logs = await GetAuditLogsAsync(directeur);

        logs.Items.Should().ContainSingle(l => l.Module == "Auth" && l.Action == "Login" && l.Success);
        logs.TotalCount.Should().Be(1);
    }
}
