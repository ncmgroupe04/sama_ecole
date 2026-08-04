using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// Console Super Admin — /admin/platform/dashboard et /admin/platform/activity (migration
/// AddPlatformAdminViews). Critère central : le Super Admin n'a AUCUN SchoolId propre, donc la RLS
/// PostgreSQL ferme normalement schools/users/subscriptions/subscription_payments/audit_logs pour sa
/// session (AGENTS.md règle #2) — ces deux endpoints doivent malgré tout renvoyer des données
/// AGRÉGÉES SUR TOUTES LES ÉCOLES, via la vue et la fonction SECURITY DEFINER de la migration, jamais
/// via un affaiblissement des policies existantes (vérifié indirectement : les tests d'isolation
/// multi-tenant des autres modules continuent de protéger ces mêmes tables pour tout autre rôle).
/// </summary>
public class PlatformEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);

    private record PlatformDashboardStatsDto(
        int TotalSchools, int TotalUsers, decimal TotalRevenue, int ActiveSubscriptions,
        decimal MRR, decimal ARR, decimal ForecastedRevenue30Days, decimal ARPU);

    private record MonthlyRevenueProjectionDto(string Month, decimal ProjectedRevenue);

    private record GlobalAuditLogItem(
        Guid Id, Guid SchoolId, string SchoolName, Guid UserId, string ActorFullName,
        string Module, string Action, bool Success, string? FailureReason,
        string? IpAddress, DateTimeOffset OccurredAt);

    private record PaginatedGlobalAuditLogs(List<GlobalAuditLogItem> Items, int TotalCount, int Page, int PageSize);

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> SuperAdminTokenAsync() => TokenAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
    private Task<string> DirecteurTokenAsync() => TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private async Task<HttpResponseMessage> GetAsync(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    /// <summary>Sème une seconde école, hors de celle de test (EcoleId), avec un abonnement ACTIF et
    /// un paiement CONFIRMÉ — le strict minimum pour prouver l'agrégation inter-écoles. Les paramètres
    /// optionnels (période de facturation, échéance, statut) servent aux tests des KPIs financiers
    /// (MRR/ARR/ARPU/cashflow prévisionnel) et de la projection 12 mois, sans changer le comportement
    /// des appelants existants (valeurs par défaut = comportement d'origine).</summary>
    private async Task<Guid> SeedSecondSchoolWithConfirmedRevenueAsync(
        decimal confirmedAmount,
        BillingPeriod billingPeriod = BillingPeriod.Monthly,
        DateOnly? expiresAt = null,
        SubscriptionStatus subscriptionStatus = SubscriptionStatus.Active,
        string schoolName = "École B - Test Plateforme")
    {
        var schoolId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        await factory.SeedAsOwnerAsync(async db =>
        {
            db.Schools.Add(new School { Id = schoolId, Name = schoolName });

            db.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                SchoolId = schoolId,
                Plan = SubscriptionPlan.Standard,
                Status = subscriptionStatus,
                ExpiresAt = expiresAt
            });

            db.SubscriptionPayments.Add(new SubscriptionPayment
            {
                SchoolId = schoolId,
                SubscriptionId = subscriptionId,
                Amount = confirmedAmount,
                Currency = "XOF",
                Method = SubscriptionPaymentMethod.MobileMoney,
                BillingPeriod = billingPeriod,
                Provider = "PayDunya",
                Status = SubscriptionPaymentStatus.Confirmed,
                InitiatedAt = DateTimeOffset.UtcNow,
                ConfirmedAt = DateTimeOffset.UtcNow
            });

            await Task.CompletedTask;
        });

        return schoolId;
    }

    [Fact]
    public async Task SuperAdmin_Should_See_Aggregated_Dashboard_Stats_Across_All_Schools()
    {
        // École de test (EcoleId) seedée par AuthApiFactory : aucun abonnement, aucun paiement. La
        // seconde école apporte le seul abonnement Actif et le seul paiement Confirmed de ce test —
        // des assertions EXACTES (pas juste "contient") prouvent que la vue lit bien TOUTES les
        // écoles, pas seulement une par défaut.
        await SeedSecondSchoolWithConfirmedRevenueAsync(37_000m);

        var superAdmin = await SuperAdminTokenAsync();
        var response = await GetAsync("/api/v1/admin/platform/dashboard", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = (await response.Content.ReadFromJsonAsync<PlatformDashboardStatsDto>())!;

        stats.TotalSchools.Should().Be(2, "l'école de test + celle seedée par ce test");
        stats.TotalUsers.Should().Be(5, "les 5 comptes fixes seedés par AuthApiFactory, aucun ajouté ici");
        stats.TotalRevenue.Should().Be(37_000m, "seuls les paiements CONFIRMÉS comptent comme revenu (AGENTS.md règle #11)");
        stats.ActiveSubscriptions.Should().Be(1);
    }

    [Fact]
    public async Task Financial_Kpis_Should_Only_Count_Schools_With_A_Currently_Active_Subscription()
    {
        // École de test (EcoleId) : aucun abonnement, n'apporte donc rien aux KPIs financiers.
        //
        // École A : abonnement Active, Monthly, échéance dans 10 jours — DOIT compter dans MRR ET dans
        // le cashflow prévisionnel 30j.
        // École B : abonnement Active, Yearly, échéance dans 400 jours — DOIT compter dans MRR (ramené
        // à un équivalent mensuel, Amount / 12), mais PAS dans le cashflow 30j (hors fenêtre).
        // École C : abonnement Suspended (résilié après son dernier paiement confirmé) — NE DOIT PAS
        // compter dans MRR : sans le filtre sur le statut de l'abonnement, un paiement confirmé une
        // fois compterait pour toujours (c'est le bug corrigé dans la migration AddPlatformFinancialKpis).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedSecondSchoolWithConfirmedRevenueAsync(
            25_000m, BillingPeriod.Monthly, today.AddDays(10), SubscriptionStatus.Active, "École A - KPI");
        await SeedSecondSchoolWithConfirmedRevenueAsync(
            300_000m, BillingPeriod.Yearly, today.AddDays(400), SubscriptionStatus.Active, "École B - KPI");
        await SeedSecondSchoolWithConfirmedRevenueAsync(
            50_000m, BillingPeriod.Monthly, today.AddDays(5), SubscriptionStatus.Suspended, "École C - KPI");

        var superAdmin = await SuperAdminTokenAsync();
        var response = await GetAsync("/api/v1/admin/platform/dashboard", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var stats = (await response.Content.ReadFromJsonAsync<PlatformDashboardStatsDto>())!;

        stats.MRR.Should().Be(50_000m, "25 000 (École A, Monthly) + 300 000 / 12 (École B, Yearly) — École C exclue (abonnement Suspended)");
        stats.ARR.Should().Be(600_000m, "MRR × 12");
        stats.ForecastedRevenue30Days.Should().Be(25_000m, "seule l'échéance de l'École A tombe dans les 30 prochains jours");
        stats.ARPU.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Read_The_Platform_Dashboard()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await GetAsync("/api/v1/admin/platform/dashboard", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_The_Platform_Dashboard_Without_A_Token_Should_Return_401()
    {
        var response = await GetAsync("/api/v1/admin/platform/dashboard", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuperAdmin_Should_See_Activity_From_Every_School()
    {
        // Une connexion Directeur produit une entrée Auth/Login pour l'école de test (JGK-A04). La
        // création d'une école par le Super Admin produit sa propre entrée Schools/CreateSchool,
        // attribuée à la NOUVELLE école (voir AuditLogsEndpointsTests). Deux écoles, deux entrées :
        // le total exact prouve que la fonction traverse bien les deux, sans RLS pour la traverser.
        await DirecteurTokenAsync();

        var superAdmin = await SuperAdminTokenAsync();
        var createSchool = await new Func<Task<HttpResponseMessage>>(async () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/schools")
            {
                Content = JsonContent.Create(new
                {
                    name = "École Les Filaos",
                    address = "Rue 12, Médina, Dakar",
                    phone = "+221771234567",
                    directorEmail = "directrice@filaos.sn",
                    directorFullName = "Aminata Sow"
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin);
            return await _client.SendAsync(request);
        })();
        createSchool.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await GetAsync("/api/v1/admin/platform/activity?page=1&pageSize=20", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = (await response.Content.ReadFromJsonAsync<PaginatedGlobalAuditLogs>())!;

        logs.TotalCount.Should().Be(2);
        logs.Items.Should().ContainSingle(l => l.SchoolName == "École de test" && l.Module == "Auth" && l.Action == "Login");
        logs.Items.Should().ContainSingle(l => l.SchoolName == "École Les Filaos" && l.Module == "Schools" && l.Action == "CreateSchool");
    }

    [Fact]
    public async Task Platform_Activity_Should_Paginate_Correctly()
    {
        await DirecteurTokenAsync(); // École de test : Auth/Login
        var superAdmin = await SuperAdminTokenAsync();

        var createSchool = new HttpRequestMessage(HttpMethod.Post, "/api/v1/schools")
        {
            Content = JsonContent.Create(new
            {
                name = "École Les Filaos",
                address = "Rue 12, Médina, Dakar",
                phone = "+221771234567",
                directorEmail = "directrice@filaos.sn",
                directorFullName = "Aminata Sow"
            })
        };
        createSchool.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin);
        (await _client.SendAsync(createSchool)).StatusCode.Should().Be(HttpStatusCode.Created);

        var page1 = (await (await GetAsync("/api/v1/admin/platform/activity?page=1&pageSize=1", superAdmin))
            .Content.ReadFromJsonAsync<PaginatedGlobalAuditLogs>())!;
        var page2 = (await (await GetAsync("/api/v1/admin/platform/activity?page=2&pageSize=1", superAdmin))
            .Content.ReadFromJsonAsync<PaginatedGlobalAuditLogs>())!;

        page1.Items.Should().HaveCount(1);
        page2.Items.Should().HaveCount(1);
        page1.TotalCount.Should().Be(2);
        page2.TotalCount.Should().Be(2);
        page1.Items.Single().Id.Should().NotBe(page2.Items.Single().Id, "chaque page doit renvoyer une ligne différente");
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Read_The_Platform_Activity_Log()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await GetAsync("/api/v1/admin/platform/activity", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_The_Platform_Activity_Log_Without_A_Token_Should_Return_401()
    {
        var response = await GetAsync("/api/v1/admin/platform/activity", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_Excessive_Page_Size_Should_Be_Rejected()
    {
        var superAdmin = await SuperAdminTokenAsync();

        var response = await GetAsync("/api/v1/admin/platform/activity?page=1&pageSize=1000", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------- GET /admin/platform/subscriptions

    private record PlatformSubscriptionDto(
        Guid SchoolId, string SchoolName, string Plan, string Status,
        DateOnly? ExpiresAt, decimal? LastPaymentAmountXof, DateTimeOffset? LastPaymentAt,
        string? LastPaymentBillingPeriod);

    [Fact]
    public async Task SuperAdmin_Should_See_Subscriptions_Across_All_Schools()
    {
        var schoolId = await SeedSecondSchoolWithConfirmedRevenueAsync(42_000m, BillingPeriod.Yearly);

        var superAdmin = await SuperAdminTokenAsync();
        var response = await GetAsync("/api/v1/admin/platform/subscriptions", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var subscriptions = (await response.Content.ReadFromJsonAsync<List<PlatformSubscriptionDto>>())!;

        var row = subscriptions.Should().ContainSingle(s => s.SchoolId == schoolId).Which;
        row.LastPaymentBillingPeriod.Should().Be("Yearly", "colonne « Montant Contrat » de l'écran Abonnements & Facturation");
        row.Plan.Should().Be("Standard");
        row.Status.Should().Be("Active");
        row.LastPaymentAmountXof.Should().Be(42_000m);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Read_Platform_Subscriptions()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await GetAsync("/api/v1/admin/platform/subscriptions", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_The_Platform_Subscriptions_Without_A_Token_Should_Return_401()
    {
        var response = await GetAsync("/api/v1/admin/platform/subscriptions", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- GET /admin/platform/revenue-projection

    [Fact]
    public async Task SuperAdmin_Should_See_A_Revenue_Projection_Reflecting_Real_Renewal_Dates()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstMonth = new DateOnly(today.Year, today.Month, 1);

        // École D : Monthly, échéance au 1er du mois SUIVANT — se renouvelle ensuite chaque mois
        // jusqu'à la fin de l'horizon (mois d'indice 1 à 11 inclus, jamais le mois courant, indice 0).
        await SeedSecondSchoolWithConfirmedRevenueAsync(
            10_000m, BillingPeriod.Monthly, firstMonth.AddMonths(1), SubscriptionStatus.Active, "École D - Projection");

        // École E : Yearly, échéance au 1er du mois d'indice 3 — une SEULE occurrence dans l'horizon
        // 12 mois (la suivante, +12 mois, en sort).
        await SeedSecondSchoolWithConfirmedRevenueAsync(
            120_000m, BillingPeriod.Yearly, firstMonth.AddMonths(3), SubscriptionStatus.Active, "École E - Projection");

        var superAdmin = await SuperAdminTokenAsync();
        var response = await GetAsync("/api/v1/admin/platform/revenue-projection", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var projection = (await response.Content.ReadFromJsonAsync<List<MonthlyRevenueProjectionDto>>())!;

        projection.Should().HaveCount(12, "12 points mensuels, du mois courant inclus aux 11 mois suivants");
        projection[0].Month.Should().Be(firstMonth.ToString("yyyy-MM"));
        projection[0].ProjectedRevenue.Should().Be(0m, "aucune échéance ne tombe le mois courant dans ce scénario");
        projection[1].ProjectedRevenue.Should().Be(10_000m, "seule École D (mensuelle) échoit ce mois-là");
        projection[3].ProjectedRevenue.Should().Be(130_000m, "École D (10 000, mensuelle) ET École E (120 000, annuelle) échoient toutes deux ce mois-là");
        projection[11].ProjectedRevenue.Should().Be(10_000m, "dernier mois de l'horizon : École D uniquement, École E ne revient qu'à +12 mois");
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Read_The_Revenue_Projection()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await GetAsync("/api/v1/admin/platform/revenue-projection", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reading_The_Revenue_Projection_Without_A_Token_Should_Return_401()
    {
        var response = await GetAsync("/api/v1/admin/platform/revenue-projection", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- POST /admin/platform/subscriptions/{schoolId}/remind

    private async Task<HttpResponseMessage> PostAsync(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task SuperAdmin_Should_Send_A_Payment_Reminder_To_The_School_Director()
    {
        factory.Emails.Clear();
        var superAdmin = await SuperAdminTokenAsync();

        var response = await PostAsync(
            $"/api/v1/admin/platform/subscriptions/{AuthApiFactory.EcoleId}/remind", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var email = factory.Emails.LastTo(AuthApiFactory.DirecteurEmail);
        email.Should().NotBeNull("le rappel doit partir vers le Directeur de l'établissement ciblé");
    }

    [Fact]
    public async Task Reminding_An_Unknown_School_Should_Return_404()
    {
        var superAdmin = await SuperAdminTokenAsync();

        var response = await PostAsync(
            $"/api/v1/admin/platform/subscriptions/{Guid.NewGuid()}/remind", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reminding_A_School_Without_An_Active_Director_Should_Return_409()
    {
        // Seedée sans aucun utilisateur (SeedSecondSchoolWithConfirmedRevenueAsync ne crée qu'école +
        // abonnement + paiement) : aucun Directeur à qui adresser un rappel.
        var schoolId = await SeedSecondSchoolWithConfirmedRevenueAsync(1_000m);
        var superAdmin = await SuperAdminTokenAsync();

        var response = await PostAsync($"/api/v1/admin/platform/subscriptions/{schoolId}/remind", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Send_A_Payment_Reminder()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await PostAsync(
            $"/api/v1/admin/platform/subscriptions/{AuthApiFactory.EcoleId}/remind", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------- POST /admin/platform/schools/{schoolId}/impersonate

    private record ImpersonateSchoolResultDto(string AccessToken, int ExpiresIn, Guid SchoolId, string SchoolName);

    private record AuditLogEntryDto(
        Guid Id, string ActorFullName, string Module, string Action,
        bool Success, string? FailureReason, string? IpAddress, DateTimeOffset OccurredAt);

    private record PaginatedAuditLogsDto(List<AuditLogEntryDto> Items, int TotalCount, int Page, int PageSize);

    /// <summary>Lecture des claims SANS vérification de signature : suffisant ici, le test possède déjà la clé de signature (AuthApiFactory.SigningKey) et ne cherche qu'à inspecter le contenu émis.</summary>
    private static Dictionary<string, string> DecodeJwtClaims(string token)
    {
        var payload = token.Split('.')[1];
        var padded = payload.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
    }

    [Fact]
    public async Task SuperAdmin_Should_Obtain_A_Director_Scoped_Token_When_Impersonating_A_School()
    {
        var superAdmin = await SuperAdminTokenAsync();

        var response = await PostAsync(
            $"/api/v1/admin/platform/schools/{AuthApiFactory.EcoleId}/impersonate", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse(
            "une session d'impersonation ne doit jamais recevoir de refresh token (courte durée, non renouvelable)");

        var result = (await response.Content.ReadFromJsonAsync<ImpersonateSchoolResultDto>())!;
        result.SchoolId.Should().Be(AuthApiFactory.EcoleId);

        var claims = DecodeJwtClaims(result.AccessToken);
        claims["sub"].Should().Be(AuthApiFactory.DirecteurId.ToString());
        claims["schoolId"].Should().Be(AuthApiFactory.EcoleId.ToString());
        claims["role"].Should().Be("Directeur");
        claims["impersonatedBy"].Should().Be(AuthApiFactory.SuperAdminId.ToString());

        // Le jeton fonctionne réellement comme une session Directeur (pas seulement en apparence), et
        // l'entrée d'audit obligatoire est bien visible dans le journal de CETTE école.
        var auditResponse = await GetAsync("/api/v1/audit-logs?module=Platform", result.AccessToken);
        auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = (await auditResponse.Content.ReadFromJsonAsync<PaginatedAuditLogsDto>())!;
        logs.Items.Should().ContainSingle(l => l.Action == "ImpersonateSchool" && l.Success);
    }

    [Fact]
    public async Task Impersonating_An_Unknown_School_Should_Return_404()
    {
        var superAdmin = await SuperAdminTokenAsync();

        var response = await PostAsync(
            $"/api/v1/admin/platform/schools/{Guid.NewGuid()}/impersonate", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Impersonating_A_School_Without_An_Active_Director_Should_Return_409()
    {
        var schoolId = await SeedSecondSchoolWithConfirmedRevenueAsync(1_000m);
        var superAdmin = await SuperAdminTokenAsync();

        var response = await PostAsync($"/api/v1/admin/platform/schools/{schoolId}/impersonate", superAdmin);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Impersonate_A_School()
    {
        var directeur = await DirecteurTokenAsync();

        var response = await PostAsync(
            $"/api/v1/admin/platform/schools/{AuthApiFactory.EcoleId}/impersonate", directeur);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
