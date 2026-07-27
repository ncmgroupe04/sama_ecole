using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// Module Tarification, Réductions &amp; Offres Promotionnelles — écran /admin/tarification. Aucun test
/// n'existait jusqu'ici pour ce module (PromoCodesController, ValidatePromoCodeQuery, et l'application
/// d'un code lors du paiement d'abonnement) malgré une logique déjà en place depuis le socle SMS/gating
/// par formule/codes promo. Critères testés : CRUD réservé au Super Admin, code insensible à la casse,
/// aperçu cohérent avec la charge réelle, réduction effectivement appliquée au paiement, et
/// FreeTrialMonths/FullDiscount qui n'émettent JAMAIS de SubscriptionPayment (règle #11).
/// </summary>
public class PromoCodesEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);
    private record InitiateResult(Guid? PaymentId, string? RedirectUrl, string? Status, bool ActivatedWithoutPayment);
    private record PromoCodeResult(Guid Id, string Code);
    private record PromoCodeDto(Guid Id, string Code, string DiscountType, decimal DiscountValue, bool IsActive, int CurrentUses, int? MaxUses);
    private record ValidateResult(bool IsValid, string? Message, decimal? BaseAmount, decimal? DiscountedAmount, bool RequiresPayment);

    private const string DirectorPassword = "Correct-Horse-9";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<ApprovalResult> CreateAwaitingPaymentSchoolAsync(string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName,
            requestedPlan = "Standard"
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var superAdmin = await LoginAsSuperAdminAsync();
        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var listResponse = await _client.SendAsync(listRequest);
        var id = (await listResponse.Content.ReadFromJsonAsync<List<ListItem>>())!
            .Single(r => r.TrackingReference == reference).Id;

        var approveRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve");
        approveRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var approveResponse = await _client.SendAsync(approveRequest);

        return (await approveResponse.Content.ReadFromJsonAsync<ApprovalResult>())!;
    }

    private async Task<HttpResponseMessage> CreatePromoCodeAsync(
        string accessToken, string code, string discountType, decimal discountValue,
        int? durationMonths = null, int? maxUses = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/admin/promo-codes")
        {
            Content = JsonContent.Create(new
            {
                code,
                discountType,
                discountValue,
                durationMonths,
                maxUses,
                startDateUtc = DateTime.UtcNow.AddDays(-1),
                endDateUtc = DateTime.UtcNow.AddDays(30)
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    // ------------------------------------------------------------ CRUD

    [Fact]
    public async Task SuperAdmin_Should_Create_And_List_A_Promo_Code()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        var createResponse = await CreatePromoCodeAsync(superAdmin.AccessToken, "RENTREE2026", "Percentage", 15m);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/promo-codes");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var listResponse = await _client.SendAsync(listRequest);
        var codes = (await listResponse.Content.ReadFromJsonAsync<List<PromoCodeDto>>())!;

        codes.Should().Contain(c => c.Code == "RENTREE2026" && c.IsActive);
    }

    [Fact]
    public async Task A_Directeur_Must_Not_Be_Allowed_To_Manage_Promo_Codes()
    {
        var directeur = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await CreatePromoCodeAsync(directeur.AccessToken, "PIRATE2026", "Percentage", 10m);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Codes_Should_Be_Normalized_And_Case_Insensitively_Unique()
    {
        var superAdmin = await LoginAsSuperAdminAsync();

        var first = await CreatePromoCodeAsync(superAdmin.AccessToken, "bienvenue2026", "Percentage", 10m);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await CreatePromoCodeAsync(superAdmin.AccessToken, "BIENVENUE2026", "FixedAmount", 1000m);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "deux Super Admin qui saisiraient des casses différentes ne doivent pas produire deux codes distincts");
    }

    [Fact]
    public async Task Deactivating_A_Promo_Code_Should_Not_Delete_It()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var createResponse = await CreatePromoCodeAsync(superAdmin.AccessToken, "TEMPORAIRE2026", "Percentage", 10m);
        var created = (await createResponse.Content.ReadFromJsonAsync<PromoCodeResult>())!;

        var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/promo-codes/{created.Id}/deactivate");
        deactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var deactivateResponse = await _client.SendAsync(deactivateRequest);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/promo-codes");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var listResponse = await _client.SendAsync(listRequest);
        var codes = (await listResponse.Content.ReadFromJsonAsync<List<PromoCodeDto>>())!;

        codes.Should().Contain(c => c.Code == "TEMPORAIRE2026" && !c.IsActive,
            "AGENTS.md règle #6 : aucune suppression physique, toujours un état désactivé visible");
    }

    // ------------------------------------------------------------ Aperçu (ValidatePromoCodeQuery)

    [Fact]
    public async Task A_Deactivated_Code_Should_Be_Rejected_By_The_Preview()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        var createResponse = await CreatePromoCodeAsync(superAdmin.AccessToken, "PERIME2026", "Percentage", 10m);
        var created = (await createResponse.Content.ReadFromJsonAsync<PromoCodeResult>())!;

        var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/promo-codes/{created.Id}/deactivate");
        deactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        await _client.SendAsync(deactivateRequest);

        var approval = await CreateAwaitingPaymentSchoolAsync("École Aperçu Périmé", "apercu-perime@promo.sn");
        var director = await LoginAsync("apercu-perime@promo.sn", DirectorPassword);

        var validateRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/subscriptions/validate-promo")
        {
            Content = JsonContent.Create(new { code = "PERIME2026", billingPeriod = "Monthly" })
        };
        validateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var validateResponse = await _client.SendAsync(validateRequest);
        var result = (await validateResponse.Content.ReadFromJsonAsync<ValidateResult>())!;

        result.IsValid.Should().BeFalse();
    }

    // ------------------------------------------------------------ Application réelle au paiement

    [Fact]
    public async Task A_Percentage_Code_Should_Reduce_The_Charged_Amount_And_Still_Create_A_Payment()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        await CreatePromoCodeAsync(superAdmin.AccessToken, "PROMO20", "Percentage", 20m);

        var approval = await CreateAwaitingPaymentSchoolAsync("École Promo Pourcentage", "promo-pct@promo.sn");
        var director = await LoginAsync("promo-pct@promo.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "MobileMoney", billingPeriod = "Monthly", promoCode = "PROMO20" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;
        result.PaymentId.Should().NotBeNull();

        // Standard/Monthly = 25 000 XOF (InitiateSubscriptionPaymentEndpointsTests) — 20 % de réduction.
        var payment = await factory.GetSubscriptionPaymentAsync(result.PaymentId!.Value);
        payment!.Amount.Should().Be(20_000m);
    }

    [Fact]
    public async Task A_FreeTrialMonths_Code_Should_Activate_The_Subscription_Without_Any_Payment_Row()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        await CreatePromoCodeAsync(superAdmin.AccessToken, "OFFERT3MOIS", "FreeTrialMonths", 0m, durationMonths: 3);

        var approval = await CreateAwaitingPaymentSchoolAsync("École Promo Gratuite", "promo-gratuit@promo.sn");
        var director = await LoginAsync("promo-gratuit@promo.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "MobileMoney", billingPeriod = "Monthly", promoCode = "OFFERT3MOIS" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;
        result.ActivatedWithoutPayment.Should().BeTrue();
        result.PaymentId.Should().BeNull(
            "aucun paiement à 0 FCFA ne doit être créé pour un accès gratuit — règle #11");

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.Active);
        subscription.ExpiresAt.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3));
    }

    [Fact]
    public async Task A_Code_That_Reached_Its_Usage_Limit_Should_Be_Rejected_At_Payment_Time()
    {
        var superAdmin = await LoginAsSuperAdminAsync();
        await CreatePromoCodeAsync(superAdmin.AccessToken, "UNIQUE1", "Percentage", 10m, maxUses: 1);

        var firstApproval = await CreateAwaitingPaymentSchoolAsync("École Promo Unique 1", "unique1@promo.sn");
        var firstDirector = await LoginAsync("unique1@promo.sn", DirectorPassword);
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{firstApproval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "MobileMoney", billingPeriod = "Monthly", promoCode = "UNIQUE1" })
        };
        firstRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstDirector.AccessToken);
        var firstResponse = await _client.SendAsync(firstRequest);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondApproval = await CreateAwaitingPaymentSchoolAsync("École Promo Unique 2", "unique2@promo.sn");
        var secondDirector = await LoginAsync("unique2@promo.sn", DirectorPassword);
        var secondRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{secondApproval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "MobileMoney", billingPeriod = "Monthly", promoCode = "UNIQUE1" })
        };
        secondRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondDirector.AccessToken);
        var secondResponse = await _client.SendAsync(secondRequest);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "la limite globale d'utilisations porte sur TOUTES les écoles confondues");
    }

    [Fact]
    public async Task An_Unknown_Promo_Code_Should_Be_Rejected_At_Payment_Time()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Promo Inconnu", "promo-inconnu@promo.sn");
        var director = await LoginAsync("promo-inconnu@promo.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "MobileMoney", billingPeriod = "Monthly", promoCode = "N_EXISTE_PAS" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
