using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.FunctionalTests.Common;
using Xunit;

namespace SamaEcole.FunctionalTests.Subscriptions;

/// <summary>
/// Ticket JGK-I06 — POST /webhooks/payments/{provider}. Critères testés : signature invalide -> 401,
/// rien n'est modifié ; appel IPN fait foi sur le montant (pas le corps du webhook) ; activation
/// atomique (abonnement Active + nouvelle ExpiresAt) ; idempotence (rejeu -> aucun retraitement, aucune
/// double prolongation) ; le déblocage du middleware JGK-I04 est immédiat, sans réémission de token.
/// </summary>
public class ProcessPaymentWebhookEndpointsTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
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
    private const string WebhookProvider = "FakeProvider"; // paymentService.ProviderName du FakePaymentService

    private async Task<Tokens> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private Task<Tokens> LoginAsSuperAdminAsync() =>
        LoginAsync(AuthApiFactory.SuperAdminEmail, AuthApiFactory.SuperAdminPassword);

    private async Task<ApprovalResult> CreateAwaitingPaymentSchoolAsync(
        string schoolName, string directorEmail)
    {
        var submit = await _client.PostAsJsonAsync("/api/v1/registration-requests", new
        {
            directorFullName = "Fatou Sarr",
            directorEmail,
            directorPhone = "+221771119988",
            directorPassword = DirectorPassword,
            schoolName,
            ownership = "Private", cycleProfile = "Primaire", sizeTier = "Small"
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

    private async Task<Guid> InitiatePaymentAsync(
        string directorEmail, Guid schoolId, string method = "MobileMoney", string billingPeriod = "Monthly")
    {
        var director = await LoginAsync(directorEmail, DirectorPassword);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscriptions/{schoolId}/payments")
        {
            Content = JsonContent.Create(new { method, billingPeriod })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        var response = await _client.SendAsync(request);

        return (await response.Content.ReadFromJsonAsync<InitiateResult>())!.PaymentId;
    }

    /// <summary>Webhook ENTIÈREMENT anonyme (critère du ticket) : jamais d'en-tête Authorization.</summary>
    private Task<HttpResponseMessage> PostWebhookAsync(object body) =>
        _client.PostAsJsonAsync($"/api/v1/webhooks/payments/{WebhookProvider}", body);

    // ------------------------------------------------------------ Chemin nominal

    [Fact]
    public async Task A_Valid_Webhook_Should_Confirm_The_Payment_And_Activate_The_Subscription()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Webhook OK", "webhook-ok@i06.sn");
        var paymentId = await InitiatePaymentAsync("webhook-ok@i06.sn", approval.SchoolId);
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;

        var response = await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payment = await factory.GetSubscriptionPaymentAsync(paymentId);
        payment!.Status.Should().Be(SubscriptionPaymentStatus.Confirmed);
        payment.ConfirmedAt.Should().NotBeNull();

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.Active);
        // Paiement mensuel (période par défaut de InitiatePaymentAsync) : l'abonnement est prolongé d'UN MOIS.
        subscription.ExpiresAt.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(1));
    }

    [Fact]
    public async Task Yearly_Billing_Should_Extend_The_Subscription_By_One_Year()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Webhook Annuel", "webhook-annuel@i06.sn");
        var paymentId = await InitiatePaymentAsync("webhook-annuel@i06.sn", approval.SchoolId, billingPeriod: "Yearly");
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;

        await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.ExpiresAt.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
    }

    [Fact]
    public async Task The_Middleware_Should_Unblock_The_School_Immediately_Without_A_New_Token()
    {
        // Critère JGK-I04/I06 combiné : « un token JWT émis avant paiement reste valide après paiement ».
        var approval = await CreateAwaitingPaymentSchoolAsync("École Déblocage I06", "deblocage-i06@i06.sn");
        var director = await LoginAsync("deblocage-i06@i06.sn", DirectorPassword);
        var paymentId = await InitiatePaymentAsync("deblocage-i06@i06.sn", approval.SchoolId);
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        // Toujours bloqué AVANT le webhook.
        var beforeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/students");
        beforeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        (await _client.SendAsync(beforeRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;
        await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        // MÊME token, réutilisé tel quel : débloqué sans le moindre renouvellement.
        var afterRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/students");
        afterRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", director.AccessToken);
        (await _client.SendAsync(afterRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_Confirmation_Email_Should_Be_Sent_On_Success()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École E-mail I06", "email-i06@i06.sn");
        var paymentId = await InitiatePaymentAsync("email-i06@i06.sn", approval.SchoolId);
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;
        await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        var email = factory.Emails.LastTo("email-i06@i06.sn");
        email.Should().NotBeNull();
        email!.Subject.Should().Contain("actif");
    }

    // ------------------------------------------------------------ Idempotence

    [Fact]
    public async Task A_Replayed_Webhook_Should_Not_Reprocess_The_Payment()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Rejeu I06", "rejeu-i06@i06.sn");
        var paymentId = await InitiatePaymentAsync("rejeu-i06@i06.sn", approval.SchoolId);
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;

        var first = await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });
        var subscriptionAfterFirst = await factory.GetSubscriptionAsync(approval.SchoolId);

        var second = await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });
        var subscriptionAfterSecond = await factory.GetSubscriptionAsync(approval.SchoolId);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK, "un rejeu doit être acquitté, jamais rejeté");

        subscriptionAfterSecond!.ExpiresAt.Should().Be(subscriptionAfterFirst!.ExpiresAt,
            "un second appel identique ne doit jamais prolonger une seconde fois l'abonnement");

        // Le parcours complet (I01 demande + I03 approbation + I06 activation) envoie légitimement
        // PLUSIEURS e-mails distincts à ce Directeur — seul le nombre d'e-mails D'ACTIVATION compte ici.
        factory.Emails.Sent.Count(m => m.To == "rejeu-i06@i06.sn" && m.Subject.Contains("actif")).Should().Be(1,
            "un rejeu ne doit déclencher aucun second e-mail de confirmation");
    }

    // ------------------------------------------------------------ Sécurité

    [Fact]
    public async Task An_Invalid_Signature_Should_Return_401_And_Change_Nothing()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Signature Invalide I06", "signature-i06@i06.sn");
        var paymentId = await InitiatePaymentAsync("signature-i06@i06.sn", approval.SchoolId);

        var response = await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = false });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var payment = await factory.GetSubscriptionPaymentAsync(paymentId);
        payment!.Status.Should().Be(SubscriptionPaymentStatus.Initiated, "rien ne doit changer sans signature valide");

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.AwaitingPayment);
    }

    [Fact]
    public async Task An_Amount_Mismatch_Should_Fail_The_Payment_Without_Activating_The_Subscription()
    {
        // Le montant RÉELLEMENT confirmé par l'agrégateur (pas le corps du webhook) doit correspondre au
        // montant calculé serveur à l'initiation — un écart est un échec, jamais une confirmation partielle.
        var approval = await CreateAwaitingPaymentSchoolAsync("École Montant Incorrect I06", "montant-i06@i06.sn");
        var paymentId = await InitiatePaymentAsync("montant-i06@i06.sn", approval.SchoolId);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = 1; // très différent du montant réellement dû

        var response = await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "un montant erroné est un ÉCHEC métier, pas une erreur HTTP");

        var payment = await factory.GetSubscriptionPaymentAsync(paymentId);
        payment!.Status.Should().Be(SubscriptionPaymentStatus.Failed);

        var subscription = await factory.GetSubscriptionAsync(approval.SchoolId);
        subscription!.Status.Should().Be(SubscriptionStatus.AwaitingPayment);

        // L'approbation (I03) envoie déjà un e-mail à ce Directeur — c'est spécifiquement l'e-mail
        // D'ACTIVATION (I06) qui ne doit pas exister pour un paiement en échec.
        factory.Emails.Sent.Should().NotContain(m => m.To == "montant-i06@i06.sn" && m.Subject.Contains("actif"),
            "aucun e-mail de confirmation pour un paiement en échec");
    }

    [Fact]
    public async Task An_Unpaid_Invoice_Should_Fail_The_Payment()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Non Payée I06", "nonpaye-i06@i06.sn");
        var paymentId = await InitiatePaymentAsync("nonpaye-i06@i06.sn", approval.SchoolId);
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        factory.Payments.NextConfirmationIsPaid = false; // l'agrégateur dit : pas payé
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;

        await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        var payment = await factory.GetSubscriptionPaymentAsync(paymentId);
        payment!.Status.Should().Be(SubscriptionPaymentStatus.Failed);
    }

    [Fact]
    public async Task An_Unknown_Payment_Reference_Should_Return_404()
    {
        var response = await PostWebhookAsync(new { internalPaymentId = Guid.NewGuid(), signatureValid = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Unsupported_Provider_Should_Return_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/webhooks/payments/cinetpay", new { internalPaymentId = Guid.NewGuid(), signatureValid = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_Webhook_Should_Require_No_Authentication()
    {
        var approval = await CreateAwaitingPaymentSchoolAsync("École Anonyme I06", "anonyme-i06@i06.sn");
        var paymentId = await InitiatePaymentAsync("anonyme-i06@i06.sn", approval.SchoolId);
        var storedPayment = await factory.GetSubscriptionPaymentAsync(paymentId);

        factory.Payments.NextConfirmationIsPaid = true;
        factory.Payments.NextConfirmationAmount = storedPayment!.Amount;

        // AUCUN en-tête Authorization envoyé — critère explicite du ticket.
        var response = await PostWebhookAsync(new { internalPaymentId = paymentId, signatureValid = true });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
