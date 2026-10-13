using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Registration;

/// <summary>
/// Offre PAR TRANCHE D'EFFECTIF à l'inscription : soumission (règle anti-fraude effectif ↔ tranche, « Grand Groupe »
/// au-delà de 400), approbation qui provisionne directement une souscription ACTIVE configurée (profil, modules,
/// cycles, quota) et facturation d'après la grille par tranche — avec la majoration Internat quand elle est fixée.
/// Les demandes sans tranche (grille historique) gardent leur Onboarding à faire.
/// </summary>
public class TieredRegistrationEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private const string DirectorPassword = "Correct-Horse-9";

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference, string Status, string? Profile, string? Tier, bool HasBoarding, string? Cycles);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);
    private record InitiateResult(Guid? PaymentId, string? RedirectUrl, string? Status, bool ActivatedWithoutPayment);
    private record OnboardingSubscription(string Status, string ProfileType, string StudentQuotaTier, int MaxStudentLimit, int SoftQuotaLimit,
        bool IsPedagogyEnabled, bool IsFinanceEnabled, bool IsInternatEnabled, bool IsCoranModuleEnabled);

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private Task<HttpResponseMessage> SubmitAsync(string email, object offer) =>
        _client.PostAsJsonAsync("/api/v1/registration-requests", new Dictionary<string, object?>
        {
            ["directorFullName"] = "Fatou Sarr",
            ["directorEmail"] = email,
            ["directorPhone"] = "+221771119988",
            ["directorPassword"] = DirectorPassword,
            ["schoolName"] = "École " + email,
            ["ownership"] = "Private"
        }.Concat(ToDictionary(offer)).ToDictionary(kv => kv.Key, kv => kv.Value));

    private static Dictionary<string, object?> ToDictionary(object offer) =>
        offer.GetType().GetProperties().ToDictionary(
            p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.GetValue(offer));

    private async Task<Guid> RequestIdAsync(string adminToken, string reference)
    {
        var list = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending", adminToken));
        return (await list.Content.ReadFromJsonAsync<List<ListItem>>())!.Single(r => r.TrackingReference == reference).Id;
    }

    /// <summary>Soumet puis approuve ; renvoie l'école créée et le jeton Super Admin.</summary>
    private async Task<(ApprovalResult Approval, string AdminToken)> SubmitAndApproveAsync(string email, object offer)
    {
        var submit = await SubmitAsync(email, offer);
        submit.StatusCode.Should().Be(HttpStatusCode.OK, await submit.Content.ReadAsStringAsync());
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var admin = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var id = await RequestIdAsync(admin, reference);

        var approve = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve", admin));
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());

        return ((await approve.Content.ReadFromJsonAsync<ApprovalResult>())!, admin);
    }

    private async Task<decimal> ChargedAmountAsync(string directorEmail, Guid schoolId, string billingPeriod)
    {
        var director = await LoginAsync(directorEmail, DirectorPassword);
        var response = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/v1/subscriptions/{schoolId}/payments", director,
            new { method = "MobileMoney", billingPeriod }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;
        return (await factory.GetSubscriptionPaymentAsync(result.PaymentId!.Value))!.Amount;
    }

    private async Task<(string Profile, string Tier, int Max, int Soft, string Status, bool Pedagogy, bool Finance, bool Internat, bool Coran,
        string? EstablishmentProfile, string ManagedCycles)> ReadConfigurationAsync(Guid schoolId)
    {
        (string, string, int, int, string, bool, bool, bool, bool, string?, string) result = default;

        await factory.SeedAsOwnerAsync(async db =>
        {
            var sub = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == schoolId);
            var settings = await db.SchoolSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == schoolId);

            result = (sub.ProfileType.ToString(), sub.StudentQuotaTier.ToString(), sub.MaxStudentLimit, sub.SoftQuotaLimit,
                sub.Status.ToString(), sub.IsPedagogyEnabled, sub.IsFinanceEnabled, sub.IsInternatEnabled, sub.IsCoranModuleEnabled,
                settings.ProfileEtablissement?.ToString(), settings.ManagedCycles);
        });

        return result;
    }

    // ------------------------------------------------------------------------------- Soumission

    [Fact]
    public async Task A_Tier_Smaller_Than_The_Declared_Count_Is_Refused_With_The_Minimum_Tier()
    {
        var response = await SubmitAsync("trop-petit@tranche.sn", new
        {
            Profile = "EnseignementGeneral", Tier = "Tier_100", EstimatedStudentCount = 130, Cycles = new[] { "Primaire" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("130").And.Contain("150");
    }

    [Theory]
    [InlineData("Tier2_400", 410)]
    [InlineData("Tier3_800", 120)]
    public async Task Above_400_Students_The_Offer_Points_To_The_Grand_Groupe(string tier, int count)
    {
        var response = await SubmitAsync($"grand-groupe-{tier}@tranche.sn", new
        {
            Profile = "EnseignementGeneral", Tier = tier, EstimatedStudentCount = count, Cycles = new[] { "Primaire" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Grand Groupe");
    }

    [Fact]
    public async Task A_Pending_Tiered_Request_Is_Listed_With_Its_Offer_For_The_Super_Admin()
    {
        var submit = await SubmitAsync("liste@tranche.sn", new
        {
            Profile = "FrancoArabe", Tier = "Tier_250", EstimatedStudentCount = 200, HasBoarding = true,
            Cycles = new[] { "Maternelle", "Primaire" }
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var admin = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var list = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending", admin));
        var item = (await list.Content.ReadFromJsonAsync<List<ListItem>>())!.Single(r => r.TrackingReference == reference);

        (item.Profile, item.Tier, item.HasBoarding, item.Cycles).Should().Be(("FrancoArabe", "Tier_250", true, "Maternelle,Primaire"));
    }

    // ------------------------------------------------------------------------------- Approbation

    [Fact]
    public async Task Approving_A_Tiered_Request_Provisions_An_Active_Configured_School()
    {
        var (approval, _) = await SubmitAndApproveAsync("college@tranche.sn", new
        {
            Profile = "EnseignementGeneral", Tier = "Tier1_150", EstimatedStudentCount = 120, HasBoarding = true,
            Cycles = new[] { "Primaire", "College" }
        });

        var config = await ReadConfigurationAsync(approval.SchoolId);
        config.Should().Be(("EnseignementGeneral", "Tier1_150", 150, 160, "Active",
            Pedagogy: true, Finance: true, Internat: true, Coran: false, EstablishmentProfile: "General", ManagedCycles: "Primaire,College"));

        // Le Directeur n'a aucun Onboarding à faire : la souscription est déjà Active à sa première connexion.
        var director = await LoginAsync("college@tranche.sn", DirectorPassword);
        var subscription = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/onboarding/subscription", director));
        (await subscription.Content.ReadFromJsonAsync<OnboardingSubscription>())!.Status.Should().Be("Active");
    }

    [Theory]
    [InlineData("Maternelle", "prescolaire")]   // préscolaire seulement
    [InlineData("Primaire", "elementaire")]     // élémentaire (primaire) seulement
    public async Task A_School_With_A_Single_Cycle_Starts_With_Exactly_That_Cycle(string cycle, string slug)
    {
        var (approval, _) = await SubmitAndApproveAsync($"{slug}@tranche.sn", new
        {
            Profile = "Elementaire", Tier = "Tier_100", EstimatedStudentCount = 70, Cycles = new[] { cycle }
        });

        var config = await ReadConfigurationAsync(approval.SchoolId);

        config.ManagedCycles.Should().Be(cycle);
        config.Status.Should().Be("Active");
        config.EstablishmentProfile.Should().Be("ElementairePrimaire");
    }

    [Fact]
    public async Task A_Daara_Of_Pure_Memorisation_Is_Provisioned_Without_Pedagogy_And_With_Every_Cycle_Available()
    {
        var (approval, _) = await SubmitAndApproveAsync("daara@tranche.sn", new
        {
            Profile = "DaaraMemorisation", Tier = "Tier_100", EstimatedStudentCount = 60
        });

        var config = await ReadConfigurationAsync(approval.SchoolId);
        config.Should().Be(("DaaraMemorisation", "Tier_100", 100, 110, "Active",
            Pedagogy: false, Finance: true, Internat: true, Coran: true, EstablishmentProfile: "DaaraMemorisation",
            ManagedCycles: "Maternelle,Primaire,College,Lycee"));
    }

    [Fact]
    public async Task Boarding_Switches_On_The_Internat_Module_For_A_Profile_That_Does_Not_Have_It()
    {
        var (withBoarding, _) = await SubmitAndApproveAsync("compta-internat@tranche.sn", new
        {
            Profile = "ComptabiliteRapports", Tier = "Tier_100", EstimatedStudentCount = 80, HasBoarding = true
        });
        var (without, _) = await SubmitAndApproveAsync("compta-externat@tranche.sn", new
        {
            Profile = "ComptabiliteRapports", Tier = "Tier_100", EstimatedStudentCount = 80, HasBoarding = false
        });

        (await ReadConfigurationAsync(withBoarding.SchoolId)).Internat.Should().BeTrue();
        (await ReadConfigurationAsync(without.SchoolId)).Internat.Should().BeFalse();
    }

    [Fact]
    public async Task A_Request_Without_A_Tier_Keeps_Its_Onboarding_To_Do()
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr", directorEmail = "historique@tranche.sn", directorPhone = "+221771119988",
            directorPassword = DirectorPassword, schoolName = "École historique",
            ownership = "Private", cycleProfile = "Primaire", sizeTier = "Small"
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;
        var admin = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var id = await RequestIdAsync(admin, reference);

        var approve = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve", admin));
        var approval = (await approve.Content.ReadFromJsonAsync<ApprovalResult>())!;

        (await ReadConfigurationAsync(approval.SchoolId)).Status.Should().Be("PendingOnboarding");
    }

    [Fact]
    public async Task The_Offer_Is_Checked_Again_At_Approval_And_The_Request_Stays_Pending_When_It_No_Longer_Holds()
    {
        var submit = await SubmitAsync("trafique@tranche.sn", new
        {
            Profile = "EnseignementGeneral", Tier = "Tier1_150", EstimatedStudentCount = 120, Cycles = new[] { "Primaire" }
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;
        var admin = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var id = await RequestIdAsync(admin, reference);

        // Une valeur que la soumission aurait refusée (effectif réécrit en base après coup).
        await factory.ExecuteOwnerSqlAsync(
            $"""UPDATE school_registration_requests SET "EstimatedStudentCount" = 410 WHERE "Id" = '{id}'""");

        var approve = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve", admin));

        approve.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await approve.Content.ReadAsStringAsync()).Should().Contain("Grand Groupe");
        (await RequestIdAsync(admin, reference)).Should().Be(id, "la demande reste en attente");
    }

    [Fact]
    public async Task A_Tier_With_No_Configured_Price_Blocks_The_Approval()
    {
        var submit = await SubmitAsync("sans-prix@tranche.sn", new
        {
            Profile = "ComptabiliteRapports", Tier = "Tier_250", EstimatedStudentCount = 200
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;
        var admin = await LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);
        var id = await RequestIdAsync(admin, reference);

        await factory.ExecuteOwnerSqlAsync("""UPDATE quota_tier_prices SET "IsDeleted" = true WHERE "Tier" = 'Tier_250'""");
        try
        {
            var approve = await _client.SendAsync(Authorized(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve", admin));

            // 409 : conflit avec l'état de la plateforme (grille non renseignée), pas une saisie invalide.
            approve.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await approve.Content.ReadAsStringAsync()).Should().Contain("PRICING_NOT_CONFIGURED");
        }
        finally
        {
            await factory.ExecuteOwnerSqlAsync("""UPDATE quota_tier_prices SET "IsDeleted" = false WHERE "Tier" = 'Tier_250'""");
        }
    }

    // ------------------------------------------------------------------------------- Facturation

    [Theory]
    [InlineData("Monthly", 10_000)]
    [InlineData("Yearly", 100_000)]
    public async Task A_Tiered_School_Is_Charged_The_Price_Of_Its_Tier(string billingPeriod, int expected)
    {
        var email = $"facture-{billingPeriod.ToLowerInvariant()}@tranche.sn";
        var (approval, _) = await SubmitAndApproveAsync(email, new
        {
            Profile = "EnseignementGeneral", Tier = "Tier1_150", EstimatedStudentCount = 120, Cycles = new[] { "Primaire" }
        });

        (await ChargedAmountAsync(email, approval.SchoolId, billingPeriod)).Should().Be(expected);
    }

    [Fact]
    public async Task The_Boarding_Surcharge_Set_By_The_Super_Admin_Applies_Only_To_A_School_With_Boarding()
    {
        var (boarding, admin) = await SubmitAndApproveAsync("majore@tranche.sn", new
        {
            Profile = "EnseignementGeneral", Tier = "Tier_250", EstimatedStudentCount = 200, HasBoarding = true, Cycles = new[] { "Primaire" }
        });
        var (day, _) = await SubmitAndApproveAsync("externe@tranche.sn", new
        {
            Profile = "EnseignementGeneral", Tier = "Tier_250", EstimatedStudentCount = 200, HasBoarding = false, Cycles = new[] { "Primaire" }
        });

        var set = await _client.SendAsync(Authorized(HttpMethod.Put, "/api/v1/admin/pricing-grid/Tier_250", admin,
            new { monthlyXof = 15_000, annualXof = 150_000, boardingSurchargeMonthlyXof = 2_000, boardingSurchargeAnnualXof = 20_000 }));
        set.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            (await ChargedAmountAsync("majore@tranche.sn", boarding.SchoolId, "Monthly")).Should().Be(17_000);
            (await ChargedAmountAsync("majore@tranche.sn", boarding.SchoolId, "Yearly")).Should().Be(170_000);
            (await ChargedAmountAsync("externe@tranche.sn", day.SchoolId, "Monthly")).Should().Be(15_000);
        }
        finally
        {
            await _client.SendAsync(Authorized(HttpMethod.Put, "/api/v1/admin/pricing-grid/Tier_250", admin,
                new { monthlyXof = 15_000, annualXof = 150_000, boardingSurchargeMonthlyXof = 0, boardingSurchargeAnnualXof = 0 }));
        }
    }
}
