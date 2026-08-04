using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SamaEcole.UnitTests.Payments;

/// <summary>
/// Tickets JGK-I05/I06. Aucune clé PayDunya réelle n'étant disponible (le compte marchand reste à
/// créer), ces tests stubent le transport HTTP — même pattern que HttpSchoolLogoProviderTests — pour
/// vérifier la FORME exacte des requêtes (en-têtes PAYDUNYA-*, JSON envoyé) et le traitement des
/// réponses (succès, refus métier, panne réseau) SANS jamais contacter le vrai agrégateur.
/// </summary>
public class PayDunyaPaymentServiceTests
{
    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connexion refusée (simulée).");
    }

    private sealed class StubFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }

    private static readonly PayDunyaOptions ValidConfig = new()
    {
        MasterKey = "master-key",
        PrivateKey = "private-key",
        PublicKey = "public-key",
        Token = "token",
        ApiBaseUrl = "https://app.paydunya.com/api/v1",
        CheckoutBaseUrl = "https://paydunya.com/checkout/invoice",
        PublicBaseUrl = "https://app.sama-ecole.sn"
    };

    private static PayDunyaPaymentService Service(HttpMessageHandler handler, PayDunyaOptions? config = null) =>
        new(
            new StubFactory(handler, new Uri(ValidConfig.ApiBaseUrl + "/")),
            Options.Create(config ?? ValidConfig),
            NullLogger<PayDunyaPaymentService>.Instance);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static PaymentInitiationRequest SampleRequest() => new(
        InternalPaymentId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        SchoolId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Amount: 25_000m,
        Currency: "XOF",
        Description: "Abonnement Unikol — École Test — Standard (mensuel)",
        CustomerName: "Awa Ndiaye",
        CustomerEmail: "awa@ecole-test.sn");

    [Fact]
    public void Provider_Name_Should_Be_PayDunya()
    {
        Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"))).ProviderName.Should().Be("PayDunya");
    }

    [Fact]
    public async Task A_Successful_Invoice_Should_Return_The_Checkout_Redirect_Url()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"00","response_text":"Invoice Created","token":"abc123token"}"""));

        var result = await Service(handler).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        result.ProviderTransactionRef.Should().Be("abc123token");
        result.RedirectUrl.Should().Be("https://paydunya.com/checkout/invoice/abc123token");
    }

    [Fact]
    public async Task The_Request_Should_Carry_The_Four_PayDunya_Headers()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"00","response_text":"OK","token":"tok"}"""));

        await Service(handler).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        var headers = handler.LastRequest!.Headers;
        headers.GetValues("PAYDUNYA-MASTER-KEY").Should().ContainSingle().Which.Should().Be("master-key");
        headers.GetValues("PAYDUNYA-PRIVATE-KEY").Should().ContainSingle().Which.Should().Be("private-key");
        headers.GetValues("PAYDUNYA-PUBLIC-KEY").Should().ContainSingle().Which.Should().Be("public-key");
        headers.GetValues("PAYDUNYA-TOKEN").Should().ContainSingle().Which.Should().Be("token");
    }

    [Fact]
    public async Task The_Request_Body_Should_Carry_The_Amount_Description_And_Correlation_Ids()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"00","response_text":"OK","token":"tok"}"""));

        await Service(handler).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;

        root.GetProperty("invoice").GetProperty("total_amount").GetDecimal().Should().Be(25_000m);
        root.GetProperty("invoice").GetProperty("description").GetString().Should().Contain("Standard");

        // Corrélation redondante avec le token PayDunya (ProviderTransactionRef), en préparation du
        // futur traitement du webhook (JGK-I06).
        root.GetProperty("custom_data").GetProperty("internal_payment_id").GetString()
            .Should().Be("11111111-1111-1111-1111-111111111111");
        root.GetProperty("custom_data").GetProperty("school_id").GetString()
            .Should().Be("22222222-2222-2222-2222-222222222222");

        // Callback vers la future route JGK-I06, sur l'origine publique configurée.
        root.GetProperty("actions").GetProperty("callback_url").GetString()
            .Should().Be("https://app.sama-ecole.sn/api/v1/webhooks/payments/paydunya");
    }

    [Fact]
    public async Task A_Business_Refusal_Should_Throw_Even_With_An_Http_200()
    {
        // PayDunya renvoie parfois 200 avec un response_code d'échec : le statut HTTP seul ne suffit pas.
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"01","response_text":"Solde marchand insuffisant"}"""));

        var act = () => Service(handler).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        (await act.Should().ThrowAsync<PaymentProviderException>())
            .WithMessage("*Solde marchand insuffisant*");
    }

    [Fact]
    public async Task An_Http_Error_Status_Should_Throw()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.Unauthorized, """{"response_code":"999","response_text":"Invalid keys"}"""));

        var act = () => Service(handler).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentProviderException>();
    }

    [Fact]
    public async Task A_Network_Failure_Should_Throw_A_PaymentProviderException()
    {
        var act = () => Service(new ThrowingHandler()).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentProviderException>();
    }

    [Theory]
    [InlineData("", "private-key", "public-key", "token")]
    [InlineData("master-key", "", "public-key", "token")]
    [InlineData("master-key", "private-key", "", "token")]
    [InlineData("master-key", "private-key", "public-key", "")]
    public async Task Missing_Keys_Should_Throw_Without_Any_Network_Call(
        string masterKey, string privateKey, string publicKey, string token)
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var config = new PayDunyaOptions
        {
            MasterKey = masterKey, PrivateKey = privateKey, PublicKey = publicKey, Token = token,
            ApiBaseUrl = ValidConfig.ApiBaseUrl, CheckoutBaseUrl = ValidConfig.CheckoutBaseUrl,
            PublicBaseUrl = ValidConfig.PublicBaseUrl
        };

        var act = () => Service(handler, config).InitiatePaymentAsync(SampleRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<PaymentProviderException>();
        handler.Calls.Should().Be(0, "des clés manquantes ne doivent jamais déclencher d'appel réseau");
    }

    // ------------------------------------------------------------ ParseWebhook (JGK-I06)

    private static readonly Guid SamplePaymentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>PayDunya documente le hash IPN comme SHA512(clé privée) — voir PayDunyaPaymentService.VerifyHash.</summary>
    private static string ValidHash(string privateKey) =>
        Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(privateKey)));

    /// <summary>
    /// Construit un corps de webhook via JsonSerializer plutôt qu'à la main : les accolades JSON
    /// imbriquées ({"custom_data":{...}}) entrent en conflit avec la syntaxe des chaînes brutes
    /// interpolées de C# ($$"""...{{expr}}...}}"""), ceci les évite entièrement.
    /// </summary>
    private static string WebhookJson(string? hash, Guid? internalPaymentId) =>
        JsonSerializer.Serialize(new
        {
            hash,
            custom_data = new { internal_payment_id = internalPaymentId?.ToString() }
        });

    [Fact]
    public void ParseWebhook_Should_Accept_A_Correctly_Signed_Json_Body()
    {
        var json = WebhookJson(ValidHash(ValidConfig.PrivateKey), SamplePaymentId);

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")))
            .ParseWebhook(json, "application/json");

        result.SignatureValid.Should().BeTrue();
        result.InternalPaymentId.Should().Be(SamplePaymentId);
    }

    [Fact]
    public void ParseWebhook_Should_Accept_A_Correctly_Signed_Form_Encoded_Body()
    {
        // Format PAR DÉFAUT des IPN PayDunya : notation crochets, pas de Content-Type JSON.
        var form = $"hash={ValidHash(ValidConfig.PrivateKey)}&custom_data%5Binternal_payment_id%5D={SamplePaymentId}";

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")))
            .ParseWebhook(form, "application/x-www-form-urlencoded");

        result.SignatureValid.Should().BeTrue();
        result.InternalPaymentId.Should().Be(SamplePaymentId);
    }

    [Fact]
    public void ParseWebhook_Should_Reject_An_Incorrect_Hash()
    {
        var json = WebhookJson("un-hash-quelconque", SamplePaymentId);

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")))
            .ParseWebhook(json, "application/json");

        result.SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void ParseWebhook_Should_Reject_A_Missing_Hash()
    {
        var json = WebhookJson(null, SamplePaymentId);

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")))
            .ParseWebhook(json, "application/json");

        result.SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void ParseWebhook_Should_Never_Match_When_PayDunya_Is_Not_Configured()
    {
        // Clé privée vide : aucun hash entrant ne peut jamais correspondre — échec sûr par défaut.
        var config = new PayDunyaOptions
        {
            MasterKey = "", PrivateKey = "", PublicKey = "", Token = "",
            ApiBaseUrl = ValidConfig.ApiBaseUrl, CheckoutBaseUrl = ValidConfig.CheckoutBaseUrl, PublicBaseUrl = ValidConfig.PublicBaseUrl
        };
        var json = WebhookJson(ValidHash(""), SamplePaymentId);

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")), config)
            .ParseWebhook(json, "application/json");

        result.SignatureValid.Should().BeFalse();
    }

    [Fact]
    public void ParseWebhook_Should_Not_Extract_An_Id_When_The_Signature_Is_Invalid()
    {
        // La référence n'est même pas cherchée si la signature ne correspond pas (Volume 7 §12bis :
        // vérification AVANT tout autre traitement) — vérifié ici au niveau du parsing lui-même.
        var json = WebhookJson("invalide", SamplePaymentId);

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")))
            .ParseWebhook(json, "application/json");

        result.InternalPaymentId.Should().BeNull();
    }

    [Fact]
    public void ParseWebhook_Should_Store_The_Raw_Payload_For_Audit()
    {
        var json = WebhookJson(ValidHash(ValidConfig.PrivateKey), SamplePaymentId);

        var result = Service(new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}")))
            .ParseWebhook(json, "application/json");

        result.NormalizedPayloadJson.Should().Contain(SamplePaymentId.ToString());
    }

    // ------------------------------------------------------------ ConfirmPaymentAsync (JGK-I06, IPN)

    [Fact]
    public async Task ConfirmPaymentAsync_Should_Report_Paid_For_A_Completed_Invoice()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"00","response_text":"OK","status":"completed","invoice":{"total_amount":25000}}"""));

        var result = await Service(handler).ConfirmPaymentAsync("abc123token", CancellationToken.None);

        result.IsPaid.Should().BeTrue();
        result.Amount.Should().Be(25_000m);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_Should_Report_Unpaid_For_A_Pending_Invoice()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"00","response_text":"OK","status":"pending","invoice":{"total_amount":25000}}"""));

        var result = await Service(handler).ConfirmPaymentAsync("abc123token", CancellationToken.None);

        result.IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmPaymentAsync_Should_Call_The_Confirm_Endpoint_With_The_Token_And_Headers()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK,
            """{"response_code":"00","response_text":"OK","status":"completed","invoice":{"total_amount":25000}}"""));

        await Service(handler).ConfirmPaymentAsync("abc123token", CancellationToken.None);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.ToString().Should().Contain("checkout-invoice/confirm/abc123token");
        handler.LastRequest.Headers.GetValues("PAYDUNYA-PRIVATE-KEY").Should().ContainSingle().Which.Should().Be("private-key");
    }

    [Fact]
    public async Task ConfirmPaymentAsync_Missing_Keys_Should_Throw_Without_Any_Network_Call()
    {
        var handler = new CapturingHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        var config = new PayDunyaOptions
        {
            MasterKey = "", PrivateKey = "", PublicKey = "", Token = "",
            ApiBaseUrl = ValidConfig.ApiBaseUrl, CheckoutBaseUrl = ValidConfig.CheckoutBaseUrl, PublicBaseUrl = ValidConfig.PublicBaseUrl
        };

        var act = () => Service(handler, config).ConfirmPaymentAsync("abc123token", CancellationToken.None);

        await act.Should().ThrowAsync<PaymentProviderException>();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_Network_Failure_Should_Throw_A_PaymentProviderException()
    {
        var act = () => Service(new ThrowingHandler()).ConfirmPaymentAsync("abc123token", CancellationToken.None);

        await act.Should().ThrowAsync<PaymentProviderException>();
    }
}
