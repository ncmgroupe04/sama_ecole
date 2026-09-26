using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Ticket JGK-I05 — POST /subscriptions/{schoolId}/payments. Critères testés : montant calculé serveur
/// (jamais fourni par le client) ; la ligne créée est bien liée à l'abonnement (préparation JGK-I06) ;
/// réservé au Directeur ; l'établissement de l'URL doit correspondre à la session ; un refus de
/// l'agrégateur ne laisse aucune ligne orpheline ; fonctionne aussi bien restreint que non restreint.
/// </summary>
public class InitiateSubscriptionPaymentEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task InitializeAsync() => factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);
    private record SubmitResult(string TrackingReference);
    private record ListItem(Guid Id, string TrackingReference);
    private record ApprovalResult(Guid SchoolId, Guid DirectorUserId, Guid SubscriptionId, string SubscriptionStatus);
    private record InitiateResult(Guid PaymentId, string RedirectUrl, string Status);

    private const string DirectorPassword = "Correct-Horse-9";

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    /// <summary>
    /// Fait naître une école AwaitingPayment de bout en bout via le parcours I01 -> I03. Par défaut : privé, Primaire,
    /// petit établissement, soit 150 000 FCFA/an dans la grille de la vitrine.
    /// </summary>
    private async Task<ApprovalResult> CreateAwaitingPaymentSchoolAsync(
        string schoolName, string directorEmail,
        string ownership = "Private", string cycleProfile = "Primaire", string? sizeTier = "Small")
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName,
            ownership,
            cycleProfile,
            sizeTier
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

    private async Task<HttpResponseMessage> InitiateAsync(string accessToken, Guid schoolId, string method, string billingPeriod)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{schoolId}/payments")
        {
            Content = JsonContent.Create(new { method, billingPeriod })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task A_Restricted_Director_Should_Be_Able_To_Initiate_A_Payment()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Paiement I05", "paiement-i05@test.sn");
        var director = await LoginAsync("paiement-i05@test.sn", DirectorPassword);

        var response = await InitiateAsync(director.AccessToken, approval.SchoolId, "MobileMoney", "Monthly");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;
        result.PaymentId.Should().NotBeEmpty();
        result.RedirectUrl.Should().StartWith("https://fake-checkout.example/");
        result.Status.Should().Be("Initiated");
    }

    [Fact]
    public async Task The_Amount_Should_Be_Computed_Server_Side_From_The_Grid_Offer()
    {
        // Offre « privé, Primaire, petit » (CreateAwaitingPaymentSchoolAsync) = 150 000 XOF par an dans la grille de la
        // vitrine (appsettings.json, SubscriptionPricing:Grid — critère : jamais fourni par le client). La grille est
        // annuelle : un paiement « Monthly » demandé est facturé à l'année.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Tarif I05", "tarif-i05@test.sn");
        var director = await LoginAsync("tarif-i05@test.sn", DirectorPassword);

        var response = await InitiateAsync(director.AccessToken, approval.SchoolId, "BankTransfer", "Monthly");
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;

        var payment = await factory.GetSubscriptionPaymentAsync(result.PaymentId);
        payment.Should().NotBeNull();
        payment!.Amount.Should().Be(150_000m);
        payment.BillingPeriod.Should().Be(BillingPeriod.Yearly, "la grille n'a pas de tarif mensuel");
        payment.SubscriptionId.Should().Be(approval.SubscriptionId,
            "la transaction de paiement doit être liée à l'abonnement (préparation du callback JGK-I06)");
        payment.Provider.Should().Be("FakeProvider");
        payment.Status.Should().Be(SubscriptionPaymentStatus.Initiated);
        payment.ProviderTransactionRef.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Primaire", "Large", 350_000)]
    [InlineData("College", "Medium", 400_000)]
    [InlineData("Lycee", "Small", 300_000)]
    [InlineData("Bicycle", "Large", 850_000)]
    [InlineData("Complexe", "Large", 1_200_000)]
    public async Task Each_Offer_Of_The_Grid_Is_Billed_At_Its_Published_Annual_Amount(string cycleProfile, string sizeTier, int expected)
    {
        var email = $"grille-{cycleProfile}-{sizeTier}@test.sn".ToLowerInvariant();
        var approval = await CreateAwaitingPaymentSchoolAsync($"École Grille {cycleProfile} {sizeTier}", email, "Private", cycleProfile, sizeTier);
        var director = await LoginAsync(email, DirectorPassword);

        var response = await InitiateAsync(director.AccessToken, approval.SchoolId, "BankTransfer", "Yearly");
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;

        (await factory.GetSubscriptionPaymentAsync(result.PaymentId))!.Amount.Should().Be(expected);
    }

    [Fact]
    public async Task A_Public_School_Cannot_Pay_Online_Its_Tariff_Is_Per_Student_On_Quote()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync(
            "CEM Public Tarif", "cem-public@test.sn", "Public", "College", sizeTier: null);
        var director = await LoginAsync("cem-public@test.sn", DirectorPassword);

        var response = await InitiateAsync(director.AccessToken, approval.SchoolId, "BankTransfer", "Yearly");

        response.StatusCode.Should().Be((HttpStatusCode)422);
        (await response.Content.ReadAsStringAsync()).Should().Contain("devis");
    }

    [Fact]
    public async Task The_Client_Cannot_Override_The_Amount()
    {
        // Aucun champ "amount" n'existe dans le contrat (InitiateSubscriptionPaymentCommand) : même en
        // l'injectant dans le corps de la requête, il est ignoré — le montant reste celui du barème serveur.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Anti-Triche I05", "antitriche-i05@test.sn");
        var director = await LoginAsync("antitriche-i05@test.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = "Card", billingPeriod = "Monthly", amount = 1 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);
        var result = (await response.Content.ReadFromJsonAsync<InitiateResult>())!;

        var payment = await factory.GetSubscriptionPaymentAsync(result.PaymentId);
        payment!.Amount.Should().Be(150_000m, "le montant vient du barème serveur, jamais du corps de la requête");
    }

    [Fact]
    public async Task An_Unrestricted_Active_School_Should_Also_Be_Able_To_Initiate_A_Payment()
    {
        // Renouvellement (Volume 1 §11.6) : le même mécanisme doit fonctionner hors mode restreint.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Renouvellement I05", "renouvellement-i05@test.sn");
        await factory.SetSubscriptionStatusAsync(approval.SchoolId, SubscriptionStatus.Active);
        var director = await LoginAsync("renouvellement-i05@test.sn", DirectorPassword);

        var response = await InitiateAsync(director.AccessToken, approval.SchoolId, "Card", "Yearly");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Provider_Refusal_Should_Not_Leave_Any_Orphan_Row()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Refus I05", "refus-i05@test.sn");
        var director = await LoginAsync("refus-i05@test.sn", DirectorPassword);

        factory.Payments.FailureMessage = "Solde marchand insuffisant (simulé).";
        try
        {
            var response = await InitiateAsync(director.AccessToken, approval.SchoolId, "MobileMoney", "Monthly");

            response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        }
        finally
        {
            factory.Payments.FailureMessage = null;
        }
    }

    [Fact]
    public async Task A_Secretariat_Must_Not_Be_Allowed_To_Initiate_A_Payment()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Secrétariat I05", "secretariat-i05@test.sn");
        await factory.CreateAdditionalUserAsync(approval.SchoolId, "secr-i05@test.sn", "Correct-Horse-9", Role.Secretariat);
        var secretary = await LoginAsync("secr-i05@test.sn", "Correct-Horse-9");

        var response = await InitiateAsync(secretary.AccessToken, approval.SchoolId, "MobileMoney", "Monthly");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Targeting_Another_Schools_Url_Should_Be_Rejected()
    {
        var approvalA = await CreateAwaitingPaymentSchoolAsync("École A I05", "ecole-a-i05@test.sn");
        var approvalB = await CreateAwaitingPaymentSchoolAsync("École B I05", "ecole-b-i05@test.sn");
        var directorA = await LoginAsync("ecole-a-i05@test.sn", DirectorPassword);

        // Le Directeur A tente d'initier un paiement pour l'école B en trafiquant l'URL.
        var response = await InitiateAsync(directorA.AccessToken, approvalB.SchoolId, "MobileMoney", "Monthly");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_School_Without_Any_Subscription_Should_Return_404()
    {
        // École semée par AuthApiFactory (parcours JGK-B01) : aucune ligne Subscriptions.
        var director = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

        var response = await InitiateAsync(director.AccessToken, AuthApiFactory.EcoleId, "MobileMoney", "Monthly");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Out_Of_Range_Method_Should_Return_422()
    {
        // JsonStringEnumConverter accepte aussi la valeur numérique sous-jacente (comportement par
        // défaut) : une chaîne inconnue ("Bitcoin") ferait déjà échouer la DÉSÉRIALISATION elle-même
        // (400 automatique d'[ApiController], avant même le pipeline MediatR) — un entier hors énumération
        // est le seul moyen d'atteindre réellement la règle IsInEnum() de InitiateSubscriptionPaymentValidator.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Méthode Invalide I05", "methode-i05@test.sn");
        var director = await LoginAsync("methode-i05@test.sn", DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{approval.SchoolId}/payments")
        {
            Content = JsonContent.Create(new { method = 999, billingPeriod = "Monthly" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
