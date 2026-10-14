using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Paiement d'abonnement par le compte marchand Wave de la plateforme (SubscriptionPaymentMethod.WaveMerchant). Un lien
/// marchand statique n'émet aucun webhook : la demande reste Initiated jusqu'à la validation MANUELLE du Super Admin
/// (AGENTS.md règle #11 : jamais le client). Critères testés : aucun appel à l'agrégateur ; montant calculé serveur ;
/// une seule demande en attente par école ; validation = abonnement activé ; rejet = rien n'est activé ; le Directeur ne
/// peut ni lister ni valider ; un paiement d'agrégateur n'est jamais validable à la main.
/// </summary>
public class WaveMerchantPaymentEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);
    private record WaveInfo(string PaymentUrl, decimal Amount, string Reference);
    private record InitiateResult(Guid PaymentId, string? RedirectUrl, string Status, WaveInfo? Wave);
    private record PendingWave(
        Guid PaymentId, Guid SchoolId, string SchoolName, string? DirectorFullName, string? DirectorEmail,
        string? DirectorPhone, decimal Amount, string BillingPeriod, string Reference, DateTimeOffset InitiatedAt);

    private const string DirectorPassword = "Correct-Horse-9";
    private const string ConfiguredWaveUrl = "https://pay.wave.com/m/M_RllSqNXcTsCF/c/sn/";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private HttpRequestMessage Authorized(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    /// <summary>École AwaitingPayment : privé, Primaire, petit — 150 000 FCFA/an, 12 500 FCFA/mois (grille de la vitrine).</summary>
    private async Task<ApprovalResult> CreateAwaitingPaymentSchoolAsync(string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName,
            ownership = "Private",
            cycleProfile = "Primaire",
            sizeTier = "Small"
        });
        var reference = (await submit.Content.ReadFromJsonAsync<SubmitResult>())!.TrackingReference;

        var superAdmin = await LoginAsSuperAdminAsync();
        var listResponse = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/registration-requests?status=Pending", superAdmin.AccessToken));
        var id = (await listResponse.Content.ReadFromJsonAsync<List<ListItem>>())!
            .Single(r => r.TrackingReference == reference).Id;

        var approveResponse = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/registration-requests/{id}/approve", superAdmin.AccessToken));

        return (await approveResponse.Content.ReadFromJsonAsync<ApprovalResult>())!;
    }

    private async Task<InitiateResult> InitiateAsync(string accessToken, Guid schoolId, string method, string billingPeriod)
    {
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/subscriptions/{schoolId}/payments", accessToken, new { method, billingPeriod }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<InitiateResult>())!;
    }

    private async Task<List<PendingWave>> ListPendingAsync(string superAdminToken)
    {
        var response = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/platform/wave-payments", superAdminToken));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<PendingWave>>())!;
    }

    private Task<HttpResponseMessage> ReviewAsync(string superAdminToken, Guid paymentId, string decision) =>
        _client.SendAsync(Authorized(HttpMethod.Post, $"/api/v1/admin/platform/wave-payments/{paymentId}/{decision}", superAdminToken));

    [Fact]
    public async Task Initiating_A_Wave_Payment_Returns_The_Merchant_Instructions_Without_Calling_The_Aggregator()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Init", "wave-init@test.sn");
        var director = await LoginAsync("wave-init@test.sn", DirectorPassword);
        var aggregatorCallsBefore = factory.Payments.Received.Count;

        var result = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");

        result.RedirectUrl.Should().BeNull("il n'y a pas de guichet d'agrégateur à ouvrir");
        result.Status.Should().Be("Initiated");
        result.Wave.Should().NotBeNull();
        result.Wave!.PaymentUrl.Should().Be(ConfiguredWaveUrl);
        result.Wave.Amount.Should().Be(12_500m, "le montant vient du barème serveur");
        result.Wave.Reference.Should().StartWith("WAVE-");
        factory.Payments.Received.Count.Should().Be(aggregatorCallsBefore, "le lien Wave est statique : aucun appel à l'agrégateur");

        var payment = await factory.GetSubscriptionPaymentAsync(result.PaymentId);
        payment!.Provider.Should().Be("WaveMerchant");
        payment.Method.Should().Be(SubscriptionPaymentMethod.WaveMerchant);
        payment.Status.Should().Be(SubscriptionPaymentStatus.Initiated);
        payment.ProviderTransactionRef.Should().Be(result.Wave.Reference);
        payment.SubscriptionId.Should().Be(approval.SubscriptionId);
    }

    [Fact]
    public async Task Asking_Again_For_The_Same_Invoice_Reuses_The_Pending_Request()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Double", "wave-double@test.sn");
        var director = await LoginAsync("wave-double@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();

        var first = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");
        var second = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");

        second.PaymentId.Should().Be(first.PaymentId, "un double clic ne doit pas créer deux lignes à valider (donc deux prolongations)");
        second.Wave!.Reference.Should().Be(first.Wave!.Reference);
        (await ListPendingAsync(superAdmin.AccessToken)).Count(p => p.SchoolId == approval.SchoolId).Should().Be(1);
    }

    [Fact]
    public async Task Asking_For_Another_Period_Abandons_The_Previous_Pending_Request()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Change", "wave-change@test.sn");
        var director = await LoginAsync("wave-change@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();

        var monthly = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");
        var yearly = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Yearly");

        yearly.PaymentId.Should().NotBe(monthly.PaymentId);
        yearly.Wave!.Amount.Should().Be(150_000m);
        (await factory.GetSubscriptionPaymentAsync(monthly.PaymentId))!.Status.Should().Be(SubscriptionPaymentStatus.Failed);

        var pending = (await ListPendingAsync(superAdmin.AccessToken)).Where(p => p.SchoolId == approval.SchoolId).ToList();
        pending.Should().ContainSingle().Which.PaymentId.Should().Be(yearly.PaymentId);
    }

    [Fact]
    public async Task The_Super_Admin_Sees_The_Pending_Request_With_The_Director_Contact()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Liste", "wave-liste@test.sn");
        var director = await LoginAsync("wave-liste@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();

        var initiated = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Yearly");

        var row = (await ListPendingAsync(superAdmin.AccessToken)).Single(p => p.PaymentId == initiated.PaymentId);
        row.SchoolName.Should().Be("École Wave Liste");
        row.Amount.Should().Be(150_000m);
        row.BillingPeriod.Should().Be("Yearly");
        row.Reference.Should().Be(initiated.Wave!.Reference);
        row.DirectorFullName.Should().Be("Fatou Sarr");
        row.DirectorEmail.Should().Be("wave-liste@test.sn");
        row.DirectorPhone.Should().Be("+221771119988");
    }

    [Fact]
    public async Task Approving_A_Wave_Payment_Activates_And_Extends_The_Subscription()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Valide", "wave-valide@test.sn");
        var director = await LoginAsync("wave-valide@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();
        var initiated = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Yearly");

        var response = await ReviewAsync(superAdmin.AccessToken, initiated.PaymentId, "approve");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var payment = await factory.GetSubscriptionPaymentAsync(initiated.PaymentId);
        payment!.Status.Should().Be(SubscriptionPaymentStatus.Confirmed);
        payment.ConfirmedAt.Should().NotBeNull();
        payment.WebhookPayloadRaw.Should().Contain("wave-merchant-manual").And.Contain("approved");

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.Active);
        subscription.ExpiresAt.Should().NotBeNull();
        subscription.ExpiresAt!.Value.Should().BeAfter(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(11));

        (await ListPendingAsync(superAdmin.AccessToken)).Should().NotContain(p => p.PaymentId == initiated.PaymentId);
    }

    [Fact]
    public async Task A_Wave_Payment_Cannot_Be_Approved_Twice()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Deux Fois", "wave-deuxfois@test.sn");
        var director = await LoginAsync("wave-deuxfois@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();
        var initiated = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");

        (await ReviewAsync(superAdmin.AccessToken, initiated.PaymentId, "approve")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var expiresAfterFirst = (await factory.GetSubscriptionAsync(approval.SchoolId))!.ExpiresAt;

        var second = await ReviewAsync(superAdmin.AccessToken, initiated.PaymentId, "approve");

        second.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.GetSubscriptionAsync(approval.SchoolId))!.ExpiresAt.Should().Be(expiresAfterFirst,
            "une seconde validation ne doit jamais prolonger à nouveau l'abonnement");
    }

    [Fact]
    public async Task Rejecting_A_Wave_Payment_Activates_Nothing()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Rejet", "wave-rejet@test.sn");
        var director = await LoginAsync("wave-rejet@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();
        var initiated = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");

        var response = await ReviewAsync(superAdmin.AccessToken, initiated.PaymentId, "reject");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await factory.GetSubscriptionPaymentAsync(initiated.PaymentId))!.Status.Should().Be(SubscriptionPaymentStatus.Failed);
        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.AwaitingPayment);
    }

    [Fact]
    public async Task A_Director_Cannot_List_Or_Review_Wave_Payments()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Interdit", "wave-interdit@test.sn");
        var director = await LoginAsync("wave-interdit@test.sn", DirectorPassword);
        var initiated = await InitiateAsync(director.AccessToken, approval.SchoolId, "WaveMerchant", "Monthly");

        var list = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/admin/platform/wave-payments", director.AccessToken));
        var approve = await ReviewAsync(director.AccessToken, initiated.PaymentId, "approve");

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        approve.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.GetSubscriptionPaymentAsync(initiated.PaymentId))!.Status.Should().Be(SubscriptionPaymentStatus.Initiated,
            "le client ne confirme jamais son propre paiement (AGENTS.md règle #11)");
    }

    [Fact]
    public async Task An_Aggregator_Payment_Can_Never_Be_Approved_By_Hand()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Wave Agrégateur", "wave-agregateur@test.sn");
        var director = await LoginAsync("wave-agregateur@test.sn", DirectorPassword);
        var superAdmin = await LoginAsSuperAdminAsync();
        var aggregatorPayment = await InitiateAsync(director.AccessToken, approval.SchoolId, "MobileMoney", "Monthly");

        (await ListPendingAsync(superAdmin.AccessToken)).Should().NotContain(p => p.PaymentId == aggregatorPayment.PaymentId);
        var response = await ReviewAsync(superAdmin.AccessToken, aggregatorPayment.PaymentId, "approve");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.GetSubscriptionPaymentAsync(aggregatorPayment.PaymentId))!.Status.Should().Be(SubscriptionPaymentStatus.Initiated,
            "seul le webhook signé de l'agrégateur peut confirmer ce paiement");
    }
}
