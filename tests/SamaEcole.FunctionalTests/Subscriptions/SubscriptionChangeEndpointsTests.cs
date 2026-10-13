using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// « Lock &amp; Upgrade » de bout en bout par l'API : le verrou des paramètres (profil, cycles, module non souscrit), l'aperçu du
/// tarif ajusté, la demande de changement (création, retrait, une seule en attente), les décisions du Super Admin — avec leurs
/// gardes (état périmé, effectif, classes) — la facturation qui suit la nouvelle tranche, et la date du premier dépassement de quota.
/// </summary>
public class SubscriptionChangeEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string DirectorPassword = "Correct-Horse-9";
    private const string Settings = "/api/v1/schools/current/settings";
    private const string ChangeRequests = "/api/v1/tenant-subscription/change-requests";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private record Tokens(string AccessToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId);
    private record InitiateResult(Guid? PaymentId);

    // ═════════════════════════════════════════════════════════════════════════ Aides

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirectorAsync() => LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
    private Task<string> SuperAdminAsync() => LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(), Json);

    private static object ModulesBody(bool internat, bool coran = false) => new
    {
        gradingScale = "20",
        studentMatriculeFormat = "ELEV-{YEAR}-{SEQ:4}",
        teacherMatriculeFormat = "ENS-{YEAR}-{SEQ:3}",
        autoLogoutMinutes = 10,
        dateFormat = "dd/MM/yyyy",
        tuitionMonthsPerYear = 9,
        allowSecretaryToManageGrading = false,
        isPedagogyEnabled = true,
        isFinanceEnabled = true,
        isInternatEnabled = internat,
        isCoranModuleEnabled = coran
    };

    private Task SetEntitlementAsync(string column, bool value) =>
        factory.ExecuteOwnerSqlAsync(
            $"""UPDATE tenant_subscriptions SET "{column}" = {(value ? "true" : "false")} WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'""");

    /// <summary>Crée une école par une demande d'inscription PAR TRANCHE puis l'approuve ; renvoie l'école et son Directeur connecté.</summary>
    private async Task<(Guid SchoolId, string Token)> CreateTieredSchoolAsync(
        string email, string tier, int count, string[]? cycles = null, string profile = "EnseignementGeneral")
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail = email,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName = "École " + email,
            ownership = "Private",
            profile,
            tier,
            estimatedStudentCount = count,
            cycles = cycles ?? ["Primaire"]
        });
        submit.StatusCode.Should().Be(HttpStatusCode.OK, await submit.Content.ReadAsStringAsync());
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var admin = await SuperAdminAsync();
        var list = await SendAsync(HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending", admin);
        var id = (await list.Content.ReadFromJsonAsync<List<ListItem>>())!.Single(r => r.TrackingReference == reference).Id;

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve", admin);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        var schoolId = (await approve.Content.ReadFromJsonAsync<ApprovalResult>())!.SchoolId;

        // L'abonnement de FACTURATION naît en attente de paiement et restreint l'API : on l'active pour exercer les routes.
        await factory.SetSubscriptionStatusAsync(schoolId, SubscriptionStatus.Active);

        return (schoolId, await LoginAsync(email, DirectorPassword));
    }

    private async Task<(string Tier, int Max, int Soft, bool Pedagogy, bool Internat, bool HasBoarding, string Cycles, DateTimeOffset? OverQuotaSince)>
        ReadSchoolAsync(Guid schoolId)
    {
        (string, int, int, bool, bool, bool, string, DateTimeOffset?) result = default;

        await factory.SeedAsOwnerAsync(async db =>
        {
            var sub = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == schoolId);
            var settings = await db.SchoolSettings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId);

            result = (sub.StudentQuotaTier.ToString(), sub.MaxStudentLimit, sub.SoftQuotaLimit, sub.IsPedagogyEnabled,
                sub.IsInternatEnabled, sub.HasBoarding, settings?.ManagedCycles ?? string.Empty, sub.OverQuotaSince);
        });

        return result;
    }

    private async Task<decimal> ChargedAmountAsync(string token, Guid schoolId, string billingPeriod)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/v1/subscriptions/{schoolId}/payments", token,
            new { method = "MobileMoney", billingPeriod });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var paymentId = (await response.Content.ReadFromJsonAsync<InitiateResult>())!.PaymentId!.Value;
        return (await factory.GetSubscriptionPaymentAsync(paymentId))!.Amount;
    }

    private static IEnumerable<string?> Fields(JsonElement changes) =>
        changes.EnumerateArray().Select(c => c.GetProperty("field").GetString()).ToList();

    // ═════════════════════════════════════════════════════════════════════════ Le verrou des paramètres

    [Fact]
    public async Task Enabling_A_Module_The_Subscription_Does_Not_Include_Is_Intercepted_And_Nothing_Is_Written()
    {
        var token = await DirectorAsync();
        await SetEntitlementAsync("IsInternatEnabled", false);

        var response = await SendAsync(HttpMethod.Put, Settings, token, ModulesBody(internat: true));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await ReadAsync(response);
        error.GetProperty("code").GetString().Should().Be("SUBSCRIPTION_CHANGE_REQUIRED");
        error.GetProperty("message").GetString().Should().Contain("mise à jour de votre abonnement est nécessaire");

        var preview = error.GetProperty("details");
        preview.GetProperty("hasChanges").GetBoolean().Should().BeTrue();
        preview.GetProperty("isDowngrade").GetBoolean().Should().BeFalse();
        Fields(preview.GetProperty("changes")).Should().Contain("Internat");
        preview.GetProperty("request").GetProperty("isInternatEnabled").GetBoolean().Should().BeTrue(
            "le corps de la demande à envoyer est rendu tel quel pour la modale");

        (await ReadAsync(await SendAsync(HttpMethod.Get, Settings, token))).GetProperty("isInternatEnabled").GetBoolean()
            .Should().BeFalse("rien n'a été écrit");
    }

    [Fact]
    public async Task A_Module_Included_In_The_Subscription_Is_Switched_On_And_Off_Freely()
    {
        var token = await DirectorAsync();

        (await SendAsync(HttpMethod.Put, Settings, token, ModulesBody(internat: true))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, Settings, token, ModulesBody(internat: false))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, Settings, token, ModulesBody(internat: true))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Saving_Other_Settings_Does_Not_Flag_A_Module_That_Is_Already_Active()
    {
        var token = await DirectorAsync();
        (await SendAsync(HttpMethod.Put, Settings, token, ModulesBody(internat: false))).StatusCode.Should().Be(HttpStatusCode.OK);

        // École antérieure au verrou : l'Internat est actif dans ses réglages sans figurer dans ses droits.
        await SetEntitlementAsync("IsInternatEnabled", false);
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE school_settings SET "IsInternatEnabled" = true WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'""");

        (await SendAsync(HttpMethod.Put, Settings, token, ModulesBody(internat: true))).StatusCode.Should().Be(HttpStatusCode.OK,
            "un simple enregistrement ne doit pas redemander un module déjà actif");
    }

    [Fact]
    public async Task Changing_The_Profile_Is_Intercepted_But_Reapplying_The_Current_One_Does_Nothing()
    {
        var token = await DirectorAsync();

        var change = await SendAsync(HttpMethod.Post, Settings + "/establishment-profile", token, new { profile = "FrancoArabe" });

        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var preview = (await ReadAsync(change)).GetProperty("details");
        Fields(preview.GetProperty("changes")).Should().Contain("Profile");
        preview.GetProperty("request").GetProperty("profile").GetString().Should().Be("FrancoArabe");

        (await SendAsync(HttpMethod.Post, Settings + "/establishment-profile", token, new { profile = "General" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═════════════════════════════════════════════════════════════════════════ Aperçu du tarif ajusté

    [Fact]
    public async Task The_Preview_Of_A_Bigger_Tier_Shows_The_Adjusted_Tariff_And_The_Difference()
    {
        var (_, token) = await CreateTieredSchoolAsync("apercu@changement.sn", "Tier1_150", 120);

        var response = await SendAsync(HttpMethod.Post, ChangeRequests + "/preview", token, new { tier = "Tier_250" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var preview = await ReadAsync(response);
        preview.GetProperty("hasChanges").GetBoolean().Should().BeTrue();
        preview.GetProperty("isDowngrade").GetBoolean().Should().BeFalse();
        preview.GetProperty("canRequest").GetBoolean().Should().BeTrue();
        preview.GetProperty("current").GetProperty("monthlyXof").GetDecimal().Should().Be(10_000);
        preview.GetProperty("target").GetProperty("monthlyXof").GetDecimal().Should().Be(15_000);
        preview.GetProperty("differenceMonthlyXof").GetDecimal().Should().Be(5_000);
        preview.GetProperty("differenceAnnualXof").GetDecimal().Should().Be(50_000);
        Fields(preview.GetProperty("changes")).Should().Equal("Tier");
    }

    [Fact]
    public async Task The_Preview_Of_The_Grand_Groupe_Has_No_Comparable_Price_Because_It_Is_On_Quote()
    {
        var (_, token) = await CreateTieredSchoolAsync("grand-groupe@changement.sn", "Tier2_400", 300);

        var preview = await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests + "/preview", token, new { tier = "Tier3_800" }));

        preview.GetProperty("target").GetProperty("requiresQuote").GetBoolean().Should().BeTrue();
        preview.GetProperty("differenceMonthlyXof").ValueKind.Should().Be(JsonValueKind.Null);
        preview.GetProperty("canRequest").GetBoolean().Should().BeTrue("la demande part, le support établit le devis");
    }

    [Fact]
    public async Task A_School_That_Is_Not_Billed_By_Tier_Gets_No_Invented_Price_Difference()
    {
        var token = await DirectorAsync();

        var preview = await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests + "/preview", token, new { cycles = new[] { "Primaire" } }));

        preview.GetProperty("current").GetProperty("known").GetBoolean().Should().BeFalse();
        preview.GetProperty("differenceMonthlyXof").ValueKind.Should().Be(JsonValueKind.Null);
        preview.GetProperty("isDowngrade").GetBoolean().Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════ La demande de changement

    [Fact]
    public async Task A_Request_Can_Be_Sent_Only_One_At_A_Time_And_Withdrawn()
    {
        var (_, token) = await CreateTieredSchoolAsync("une-seule@changement.sn", "Tier1_150", 120);

        var first = await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier_250" }, note = "Nous grandissons." });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadAsync(first);
        created.GetProperty("status").GetString().Should().Be("Pending");
        created.GetProperty("directorNote").GetString().Should().Be("Nous grandissons.");
        var id = created.GetProperty("id").GetGuid();

        var second = await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier2_400" } });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(second)).GetProperty("code").GetString().Should().Be("CHANGE_REQUEST_PENDING");

        var list = await ReadAsync(await SendAsync(HttpMethod.Get, ChangeRequests, token));
        list.EnumerateArray().Should().ContainSingle().Which.GetProperty("id").GetGuid().Should().Be(id);

        var cancelled = await SendAsync(HttpMethod.Post, $"{ChangeRequests}/{id}/cancel", token);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(cancelled)).GetProperty("status").GetString().Should().Be("Cancelled");

        (await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier2_400" } }))
            .StatusCode.Should().Be(HttpStatusCode.Created, "la demande retirée ne bloque plus");
    }

    [Fact]
    public async Task A_Request_That_Changes_Nothing_Is_Refused()
    {
        var (_, token) = await CreateTieredSchoolAsync("rien@changement.sn", "Tier1_150", 120);

        var response = await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier1_150" } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_Reduction_Is_Recorded_As_A_Downgrade_For_The_Support()
    {
        var (_, token) = await CreateTieredSchoolAsync("reduction@changement.sn", "Tier2_400", 300, cycles: ["Primaire", "College"]);

        var response = await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier1_150" } });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await ReadAsync(response)).GetProperty("isDowngrade").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Only_The_Director_Sends_Requests_And_Only_The_Super_Admin_Decides_Them()
    {
        var secretary = await LoginAsync(AuthApiFactory.SecretaireEmail, AuthApiFactory.SecretairePassword);
        (await SendAsync(HttpMethod.Post, ChangeRequests, secretary, new { change = new { cycles = new[] { "Primaire" } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var director = await DirectorAsync();
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/subscription-change-requests", director)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{Guid.NewGuid()}/approve", director)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, "/api/v1/admin/over-quota-schools", director)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═════════════════════════════════════════════════════════════════════════ Les décisions du Super Admin

    [Fact]
    public async Task Approving_A_Request_Applies_The_Target_State_And_Billing_Follows_The_New_Tier()
    {
        var (schoolId, token) = await CreateTieredSchoolAsync("valide@changement.sn", "Tier1_150", 120);
        var admin = await SuperAdminAsync();

        var created = await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token,
            new { change = new { tier = "Tier_250", cycles = new[] { "Primaire", "College" } } }));
        var id = created.GetProperty("id").GetGuid();

        // Le Super Admin voit la demande avec le nom de l'école et ce qui change.
        var pending = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/subscription-change-requests?status=Pending", admin));
        var item = pending.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        item.GetProperty("schoolName").GetString().Should().Be("École valide@changement.sn");
        Fields(item.GetProperty("changes")).Should().Contain(["Tier", "Cycles"]);

        // Rien n'est écrit avant la validation.
        (await ReadSchoolAsync(schoolId)).Should().Be(("Tier1_150", 150, 160, true, false, false, "Primaire", null));

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin, new { note = "Règlement reçu." });

        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        var decided = await ReadAsync(approve);
        decided.GetProperty("status").GetString().Should().Be("Approved");
        decided.GetProperty("reviewNote").GetString().Should().Be("Règlement reçu.");

        (await ReadSchoolAsync(schoolId)).Should().Be(("Tier_250", 250, 260, true, false, false, "Primaire,College", null));

        var mine = await ReadAsync(await SendAsync(HttpMethod.Get, ChangeRequests, token));
        mine.EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Approved");

        // La facturation suit la tranche COURANTE : 15 000 FCFA par mois (et non plus les 10 000 de la tranche précédente).
        await factory.SetSubscriptionStatusAsync(schoolId, SubscriptionStatus.AwaitingPayment);
        (await ChargedAmountAsync(token, schoolId, "Monthly")).Should().Be(15_000);
    }

    [Fact]
    public async Task Approving_A_Request_Emails_The_Director_With_What_Changed()
    {
        var (_, token) = await CreateTieredSchoolAsync("mail-valide@changement.sn", "Tier1_150", 120);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier_250" } })))
            .GetProperty("id").GetGuid();
        factory.Emails.Clear();

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin, new { note = "Règlement reçu." });

        approve.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(approve)).GetProperty("directorNotified").GetBoolean().Should().BeTrue();

        var mail = factory.Emails.LastTo("mail-valide@changement.sn");
        mail.Should().NotBeNull();
        mail!.Subject.Should().Contain("validée");
        mail.Body.Should().Contain("Fatou Sarr").And.Contain("jusqu'à 150 élèves → jusqu'à 250 élèves").And.Contain("Règlement reçu.");
        mail.Body.Should().NotContain("Correct-Horse-9", "aucun secret ne circule par e-mail");
    }

    [Fact]
    public async Task Rejecting_A_Request_Emails_The_Director_The_Reason()
    {
        var (_, token) = await CreateTieredSchoolAsync("mail-rejet@changement.sn", "Tier1_150", 120);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier_250" } })))
            .GetProperty("id").GetGuid();
        factory.Emails.Clear();

        var reject = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/reject", admin, new { note = "Règlement non reçu." });

        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        var mail = factory.Emails.LastTo("mail-rejet@changement.sn");
        mail.Should().NotBeNull();
        mail!.Subject.Should().Contain("n'a pas été retenue");
        mail.Body.Should().Contain("Motif : Règlement non reçu.").And.Contain("reste inchangé");
    }

    [Fact]
    public async Task A_Request_Without_A_Recorded_Address_Is_Decided_Without_Email_And_The_Super_Admin_Is_Told()
    {
        var (_, token) = await CreateTieredSchoolAsync("sans-adresse@changement.sn", "Tier1_150", 120);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier_250" } })))
            .GetProperty("id").GetGuid();
        await factory.ExecuteOwnerSqlAsync($"""UPDATE subscription_change_requests SET "RequestedByEmail" = NULL WHERE "Id" = '{id}'""");
        factory.Emails.Clear();

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin);

        approve.StatusCode.Should().Be(HttpStatusCode.OK, "la décision ne dépend pas de l'e-mail");
        (await ReadAsync(approve)).GetProperty("directorNotified").GetBoolean().Should().BeFalse();
        factory.Emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Approving_An_Internat_Request_Sets_The_Boarding_Flag_And_The_Module_And_Adds_The_Surcharge_To_The_Bill()
    {
        var (schoolId, token) = await CreateTieredSchoolAsync("internat@changement.sn", "Tier_250", 200);
        var admin = await SuperAdminAsync();

        // Majoration Internat fixée par le Super Admin dans la grille.
        (await SendAsync(HttpMethod.Put, "/api/v1/admin/pricing-grid/Tier_250", admin,
            new { monthlyXof = 15_000, annualXof = 150_000, boardingSurchargeMonthlyXof = 2_000, boardingSurchargeAnnualXof = 20_000 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            var preview = await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests + "/preview", token, new { isInternatEnabled = true }));
            preview.GetProperty("differenceMonthlyXof").GetDecimal().Should().Be(2_000, "l'internat ajoute sa majoration à la facture");

            var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { isInternatEnabled = true } })))
                .GetProperty("id").GetGuid();
            (await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin)).StatusCode
                .Should().Be(HttpStatusCode.OK);

            var school = await ReadSchoolAsync(schoolId);
            (school.Internat, school.HasBoarding).Should().Be((true, true));

            await factory.SetSubscriptionStatusAsync(schoolId, SubscriptionStatus.AwaitingPayment);
            (await ChargedAmountAsync(token, schoolId, "Monthly")).Should().Be(17_000);
        }
        finally
        {
            await SendAsync(HttpMethod.Put, "/api/v1/admin/pricing-grid/Tier_250", admin,
                new { monthlyXof = 15_000, annualXof = 150_000, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 });
        }
    }

    [Fact]
    public async Task A_School_Without_A_Settings_Row_Can_Still_Have_Its_Request_Approved_And_The_Row_Is_Created()
    {
        // L'école de test n'a PAS de ligne de réglages (comme les écoles les plus anciennes, ou celles dont le Directeur n'a jamais
        // enregistré les paramètres) : elle gère alors tous les cycles, et sa demande ne doit pas être refusée comme « périmée ».
        var token = await DirectorAsync();
        var admin = await SuperAdminAsync();
        (await ReadSchoolAsync(AuthApiFactory.EcoleId)).Cycles.Should().BeEmpty("point de départ : aucune ligne de réglages");

        var created = await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { cycles = new[] { "Primaire", "College" } } });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await ReadAsync(created)).GetProperty("id").GetGuid();

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin);

        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        (await ReadSchoolAsync(AuthApiFactory.EcoleId)).Cycles.Should().Be("Primaire,College");
        Names(await ReadAsync(await SendAsync(HttpMethod.Get, Settings, token))).Should().Equal("Primaire", "College");
    }

    private static IEnumerable<string?> Names(JsonElement settings) =>
        settings.GetProperty("managedCycles").EnumerateArray().Select(c => c.GetString()).ToList();

    [Fact]
    public async Task Rejecting_A_Request_Needs_A_Reason_And_Leaves_The_Subscription_Untouched()
    {
        var (schoolId, token) = await CreateTieredSchoolAsync("rejet@changement.sn", "Tier1_150", 120);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier_250" } })))
            .GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/reject", admin, new { note = " " }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var rejected = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/reject", admin,
            new { note = "Règlement non reçu." });

        rejected.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadAsync(rejected);
        body.GetProperty("status").GetString().Should().Be("Rejected");
        body.GetProperty("reviewNote").GetString().Should().Be("Règlement non reçu.");
        (await ReadSchoolAsync(schoolId)).Tier.Should().Be("Tier1_150");

        // Une demande décidée ne se décide pas deux fois.
        (await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Request_Whose_School_Has_Changed_Since_Is_Refused_As_Stale()
    {
        var (schoolId, token) = await CreateTieredSchoolAsync("perimee@changement.sn", "Tier1_150", 120);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier_250" } })))
            .GetProperty("id").GetGuid();

        // Entre-temps, le Super Admin a changé la tranche par l'écran des écoles.
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE tenant_subscriptions SET "StudentQuotaTier" = 'Tier2_400', "MaxStudentLimit" = 400, "SoftQuotaLimit" = 420 WHERE "SchoolId" = '{schoolId}'""");

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin);

        approve.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(approve)).GetProperty("code").GetString().Should().Be("STALE_CHANGE_REQUEST");
        (await ReadSchoolAsync(schoolId)).Tier.Should().Be("Tier2_400", "la demande périmée n'a rien écrasé");

        var pending = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/subscription-change-requests?status=Pending", admin));
        pending.EnumerateArray().Should().ContainSingle(r => r.GetProperty("id").GetGuid() == id, "elle reste en attente, à rejeter");
    }

    [Fact]
    public async Task A_Cycle_Removal_Is_Refused_At_Approval_If_Classrooms_Appeared_After_The_Request()
    {
        var (schoolId, token) = await CreateTieredSchoolAsync("classes@changement.sn", "Tier1_150", 120, cycles: ["Primaire", "College"]);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { cycles = new[] { "Primaire" } } })))
            .GetProperty("id").GetGuid();

        (await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token, new { name = "6e A", level = "Collège", capacity = 40 }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin);

        approve.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(approve)).GetProperty("code").GetString().Should().Be("CYCLE_HAS_CLASSROOMS");
        (await ReadSchoolAsync(schoolId)).Cycles.Should().Be("Primaire,College");
    }

    [Fact]
    public async Task A_Request_Is_Refused_At_Approval_If_The_Headcount_Now_Exceeds_The_Requested_Tier()
    {
        var (schoolId, token) = await CreateTieredSchoolAsync("effectif@changement.sn", "Tier2_400", 300);
        var admin = await SuperAdminAsync();
        var id = (await ReadAsync(await SendAsync(HttpMethod.Post, ChangeRequests, token, new { change = new { tier = "Tier1_150" } })))
            .GetProperty("id").GetGuid();

        // Deux élèves arrivent ; on resserre le plafond demandé à 1 pour exercer la garde sans créer 150 élèves.
        var classroom = await ReadAsync(await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name = "CI A", level = "Primaire", capacity = 40 }));
        var classroomId = classroom.GetProperty("id").GetGuid();
        for (var i = 1; i <= 2; i++)
        {
            (await SendAsync(HttpMethod.Post, "/api/v1/students", token, new
            {
                fullName = $"Eleve {i}", birthDate = "2015-03-12", birthPlace = "Dakar", gender = "F",
                classroomId, guardianName = "Tuteur Test", guardianPhone = "+221771234567"
            })).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE subscription_change_requests SET "TargetMaxStudentLimit" = 1, "TargetSoftQuotaLimit" = 1 WHERE "Id" = '{id}'""");

        var approve = await SendAsync(HttpMethod.Post, $"/api/v1/admin/subscription-change-requests/{id}/approve", admin);

        approve.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync(approve)).GetProperty("code").GetString().Should().Be("TARGET_TIER_TOO_SMALL");
        (await ReadSchoolAsync(schoolId)).Tier.Should().Be("Tier2_400");
    }

    // ═════════════════════════════════════════════════════════════════════════ Quota et date du premier dépassement

    [Fact]
    public async Task A_School_Within_Its_Quota_Has_No_Suggestion_And_No_Overrun_Date()
    {
        var (_, token) = await CreateTieredSchoolAsync("dans-le-quota@changement.sn", "Tier1_150", 120);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/tenant-subscription/quota", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var quota = await ReadAsync(response);
        quota.GetProperty("state").GetString().Should().Be("WithinQuota");
        quota.GetProperty("suggestedTier").ValueKind.Should().Be(JsonValueKind.Null);
        quota.GetProperty("overQuotaSince").ValueKind.Should().Be(JsonValueKind.Null);
        quota.GetProperty("tier").GetString().Should().Be("Tier1_150");
    }

    [Fact]
    public async Task The_First_Overrun_Is_Dated_Reported_To_The_Super_Admin_And_Cleared_Once_The_School_Is_Back_Within_Its_Quota()
    {
        var token = await DirectorAsync();
        var admin = await SuperAdminAsync();

        var classroom = await ReadAsync(await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name = "CP A", level = "Primaire", capacity = 40 }));
        var classroomId = classroom.GetProperty("id").GetGuid();

        Task<HttpResponseMessage> CreateStudent(string name) => SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName = name, birthDate = "2015-03-12", birthPlace = "Dakar", gender = "F",
            classroomId, guardianName = "Tuteur Test", guardianPhone = "+221771234567"
        });

        // Tranche « 100 » à plafond nominal 1 et tolérance 2 (réglés en base pour ne pas créer 100 élèves).
        await factory.ExecuteOwnerSqlAsync(
            $"""
             UPDATE tenant_subscriptions
                SET "StudentQuotaTier" = 'Tier_100', "MaxStudentLimit" = 1, "SoftQuotaLimit" = 2
              WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'
             """);

        (await CreateStudent("Eleve Un")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateStudent("Eleve Deux")).StatusCode.Should().Be(HttpStatusCode.Created, "la tolérance admet encore cet élève");
        (await ReadSchoolAsync(AuthApiFactory.EcoleId)).OverQuotaSince.Should().BeNull("l'effectif était encore dans le plafond à ce constat");

        // Troisième création : refusée (tolérance épuisée) — et c'est là que le dépassement est constaté et daté.
        (await CreateStudent("Eleve Trois")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var since = (await ReadSchoolAsync(AuthApiFactory.EcoleId)).OverQuotaSince;
        since.Should().NotBeNull();

        // Le bandeau du Directeur : état, tranche suggérée (la suivante), date du premier constat.
        var quota = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/tenant-subscription/quota", token));
        quota.GetProperty("state").GetString().Should().Be("InTolerance");
        quota.GetProperty("currentStudentCount").GetInt32().Should().Be(2);
        quota.GetProperty("suggestedTier").GetString().Should().Be("Tier1_150");
        quota.GetProperty("overQuotaSince").GetDateTimeOffset().Should().BeCloseTo(since!.Value, TimeSpan.FromSeconds(1),
            "la date de premier constat ne bouge pas à chaque lecture");

        // Le Super Admin voit l'école en dépassement.
        var overQuota = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/over-quota-schools", admin));
        var row = overQuota.EnumerateArray().Single(r => r.GetProperty("schoolId").GetGuid() == AuthApiFactory.EcoleId);
        (row.GetProperty("studentCount").GetInt64(), row.GetProperty("state").GetString()).Should().Be((2L, "InTolerance"));
        (row.GetProperty("maxStudentLimit").GetInt32(), row.GetProperty("softQuotaLimit").GetInt32()).Should().Be((1, 2));
        row.GetProperty("overQuotaSince").ValueKind.Should().NotBe(JsonValueKind.Null);

        // Retour dans le quota (tranche supérieure) : la date est effacée dès la prochaine évaluation.
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE tenant_subscriptions SET "StudentQuotaTier" = 'Tier1_150', "MaxStudentLimit" = 150, "SoftQuotaLimit" = 160 WHERE "SchoolId" = '{AuthApiFactory.EcoleId}'""");

        var back = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/tenant-subscription/quota", token));
        back.GetProperty("state").GetString().Should().Be("WithinQuota");
        back.GetProperty("overQuotaSince").ValueKind.Should().Be(JsonValueKind.Null);
        (await ReadSchoolAsync(AuthApiFactory.EcoleId)).OverQuotaSince.Should().BeNull();

        var after = await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/admin/over-quota-schools", admin));
        after.EnumerateArray().Should().NotContain(r => r.GetProperty("schoolId").GetGuid() == AuthApiFactory.EcoleId);
    }
}
