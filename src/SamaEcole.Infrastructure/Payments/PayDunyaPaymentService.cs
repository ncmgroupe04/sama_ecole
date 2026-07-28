using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Ticket JGK-I05/I06 — implémentation PayDunya de <see cref="IPaymentService"/>, contre l'API "Checkout
/// Invoice" documentée par PayDunya (création : POST {ApiBaseUrl}/checkout-invoice/create ; confirmation
/// IPN : GET {ApiBaseUrl}/checkout-invoice/confirm/{token} ; les deux authentifiées par les 4 en-têtes
/// PAYDUNYA-*, réponse response_code="00" en cas de succès). Aucune clé sandbox n'étant disponible au
/// moment de ces tickets, cette implémentation est vérifiée par tests unitaires contre un
/// HttpMessageHandler simulé (forme exacte des requêtes, gestion des réponses d'erreur) — une
/// vérification en conditions réelles contre le bac à sable PayDunya reste à faire dès qu'un compte
/// marchand existe. C'est PRÉCISÉMENT pour cette raison que le traitement du webhook (JGK-I06) ne fait
/// JAMAIS confiance au corps du webhook lui-même pour l'état d'un paiement : même si la forme exacte de
/// ce corps s'avérait légèrement différente en conditions réelles, seul l'appel IPN serveur à serveur
/// (ConfirmPaymentAsync), au contrat plus simple et mieux maîtrisé, décide du résultat.
/// </summary>
public class PayDunyaPaymentService(
    IHttpClientFactory httpClientFactory,
    IOptions<PayDunyaOptions> options,
    ILogger<PayDunyaPaymentService> logger) : IPaymentService
{
    public const string HttpClientName = "paydunya";

    public string ProviderName => "PayDunya";

    public async Task<PaymentInitiationResult> InitiatePaymentAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken)
    {
        var config = options.Value;

        // Échec explicite et immédiat plutôt qu'une requête vouée au 401 : des clés vides ou au sentinel
        // "REMPLACER" signifient qu'aucun compte marchand n'a encore été configuré (voir
        // PayDunyaOptions.IsConfigured — en Development, DependencyInjection bascule alors sur
        // DevPaymentService plutôt que d'atteindre cette garde).
        if (!config.IsConfigured)
        {
            throw new PaymentProviderException(
                "PayDunya n'est pas configuré (clés manquantes) — voir .env.example, section PayDunya.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        var payload = new CreateInvoiceRequest(
            Invoice: new InvoiceDetails(request.Amount, request.Description),
            Store: new StoreDetails("Sama Ecole"),
            Actions: new InvoiceActions(
                CancelUrl: $"{config.PublicBaseUrl}/abonnement/paiement",
                ReturnUrl: $"{config.PublicBaseUrl}/abonnement/paiement",
                CallbackUrl: $"{config.PublicBaseUrl}/api/v1/webhooks/payments/paydunya"),
            CustomData: new Dictionary<string, string>
            {
                // Corrélation redondante avec le token PayDunya (ProviderTransactionRef) : prépare le
                // traitement du webhook (JGK-I06), qui pourra retrouver la ligne par l'un OU l'autre.
                ["internal_payment_id"] = request.InternalPaymentId.ToString(),
                ["school_id"] = request.SchoolId.ToString()
            });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "checkout-invoice/create")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Add("PAYDUNYA-MASTER-KEY", config.MasterKey);
        httpRequest.Headers.Add("PAYDUNYA-PRIVATE-KEY", config.PrivateKey);
        httpRequest.Headers.Add("PAYDUNYA-PUBLIC-KEY", config.PublicKey);
        httpRequest.Headers.Add("PAYDUNYA-TOKEN", config.Token);

        HttpResponseMessage httpResponse;

        try
        {
            httpResponse = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "PayDunya injoignable lors de la création d'une facture.");
            throw new PaymentProviderException("L'agrégateur de paiement est momentanément injoignable. Réessayez.");
        }

        var body = await httpResponse.Content.ReadFromJsonAsync<CreateInvoiceResponse>(cancellationToken)
            ?? throw new PaymentProviderException("Réponse illisible de l'agrégateur de paiement.");

        // "00" est le seul code de succès documenté par PayDunya ; tout le reste (y compris un HTTP
        // 200 avec un autre code, PayDunya renvoie parfois 200 même en cas de refus métier) est un échec.
        if (httpResponse.StatusCode != System.Net.HttpStatusCode.OK || body.ResponseCode != "00" || string.IsNullOrWhiteSpace(body.Token))
        {
            logger.LogWarning(
                "PayDunya a refusé la création de facture : {Code} — {Text}", body.ResponseCode, body.ResponseText);
            throw new PaymentProviderException(
                $"L'agrégateur de paiement a refusé la demande : {body.ResponseText}");
        }

        return new PaymentInitiationResult(body.Token, $"{config.CheckoutBaseUrl}/{body.Token}");
    }

    /// <summary>
    /// Ticket JGK-I06. PayDunya envoie ses IPN en <c>application/x-www-form-urlencoded</c> par défaut,
    /// avec des clés en notation crochets (ex. <c>data[hash]</c>, <c>custom_data[internal_payment_id]</c>).
    /// On tente d'abord un décodage JSON (au cas où l'intégration future en dépendrait), puis on retombe
    /// sur un décodage form — SANS dépendance externe (voir DecodeForm), pour rester cohérent avec le
    /// reste du projet (aucun paquet ajouté pour un besoin ponctuel).
    ///
    /// La vérification de signature EST le hash — voir VerifyHash — et se fait AVANT toute extraction de
    /// donnée métier (docs/Volume_7_Security.md §12bis) : si le hash ne correspond pas, InternalPaymentId
    /// n'est même pas recherché.
    /// </summary>
    public WebhookParseResult ParseWebhook(string rawBody, string? contentType)
    {
        var fields = (contentType ?? string.Empty).Contains("json", StringComparison.OrdinalIgnoreCase)
            ? TryDecodeJson(rawBody)
            : DecodeForm(rawBody);

        // Corps illisible dans les deux formats (JSON ou form) : bascule sur l'autre, au cas où
        // Content-Type mentirait — un webhook mal étiqueté ne doit pas être rejeté pour cette seule raison.
        if (fields.Count == 0)
        {
            fields = TryDecodeJson(rawBody);
            if (fields.Count == 0)
            {
                fields = DecodeForm(rawBody);
            }
        }

        var hash = FindField(fields, "hash", "data[hash]");
        var signatureValid = VerifyHash(hash);

        Guid? internalPaymentId = null;

        if (signatureValid)
        {
            var raw = FindField(fields, "internal_payment_id", "custom_data[internal_payment_id]", "data[custom_data][internal_payment_id]");
            if (Guid.TryParse(raw, out var parsedId))
            {
                internalPaymentId = parsedId;
            }
        }

        var normalizedPayloadJson = JsonSerializer.Serialize(fields);

        return new WebhookParseResult(signatureValid, internalPaymentId, normalizedPayloadJson);
    }

    /// <summary>
    /// PayDunya documente le hash IPN comme SHA512(clé privée) — une preuve statique que l'appelant
    /// connaît la clé privée du marchand, pas une signature calculée sur le corps de CETTE requête.
    /// Si la clé n'est pas configurée, aucun hash ne peut jamais correspondre : échec sûr par défaut.
    /// </summary>
    private bool VerifyHash(string? hashFromPayload)
    {
        var privateKey = options.Value.PrivateKey;

        if (string.IsNullOrWhiteSpace(hashFromPayload) || string.IsNullOrWhiteSpace(privateKey))
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(SHA512.HashData(Encoding.UTF8.GetBytes(privateKey)));

        // Comparaison à temps constant : une comparaison naïve (string.Equals) sort en avance dès le
        // premier caractère différent, ce qui fuit — via le temps de réponse — combien de caractères
        // du hash attendu un appelant a déjà devinés.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hashFromPayload.Trim().ToLowerInvariant()),
            Encoding.UTF8.GetBytes(expected));
    }

    private static Dictionary<string, string> TryDecodeJson(string rawBody)
    {
        var result = new Dictionary<string, string>();

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            FlattenJson(document.RootElement, "", result);
        }
        catch (JsonException)
        {
            // Pas du JSON valide : on laisse DecodeForm (ou le second essai côté appelant) prendre le relais.
        }

        return result;
    }

    /// <summary>Aplati un document JSON en clés pointées ("custom_data.internal_payment_id") pour une recherche uniforme avec le décodage form.</summary>
    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJson(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", result);
                }
                break;
            case JsonValueKind.String:
                result[prefix] = element.GetString() ?? "";
                break;
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                result[prefix] = element.ToString();
                break;
        }
    }

    /// <summary>
    /// Décodage form-urlencoded minimal, sans dépendance externe : split sur '&amp;' puis '=', décodage
    /// URL de chaque côté. Les clés en notation crochets (<c>custom_data[internal_payment_id]</c>) sont
    /// conservées TELLES QUELLES — FindField sait les reconnaître par correspondance exacte de motif.
    /// </summary>
    private static Dictionary<string, string> DecodeForm(string rawBody)
    {
        var result = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return result;
        }

        foreach (var pair in rawBody.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            result[key] = value;
        }

        return result;
    }

    /// <summary>Cherche la première clé correspondant à l'un des motifs candidats, en tolérant la forme aplatie JSON (points) et form (crochets).</summary>
    private static string? FindField(Dictionary<string, string> fields, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (fields.TryGetValue(candidate, out var value))
            {
                return value;
            }
        }

        // Dernier recours : toute clé qui SE TERMINE par l'un des motifs (ex. "data[hash]" pour "hash").
        foreach (var candidate in candidates)
        {
            var match = fields.FirstOrDefault(f =>
                f.Key.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)
                || f.Key.EndsWith($"[{candidate}]", StringComparison.OrdinalIgnoreCase));

            if (match.Key is not null)
            {
                return match.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Ticket JGK-I06 — vérification IPN serveur à serveur (docs/Volume_7_Security.md §12bis). Utilise
    /// NOS propres clés pour interroger PayDunya, jamais une donnée relue depuis le webhook entrant.
    /// </summary>
    public async Task<PaymentConfirmationResult> ConfirmPaymentAsync(
        string providerTransactionRef, CancellationToken cancellationToken)
    {
        var config = options.Value;

        if (!config.IsConfigured)
        {
            throw new PaymentProviderException(
                "PayDunya n'est pas configuré (clés manquantes) — voir .env.example, section PayDunya.");
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get, $"checkout-invoice/confirm/{Uri.EscapeDataString(providerTransactionRef)}");
        httpRequest.Headers.Add("PAYDUNYA-MASTER-KEY", config.MasterKey);
        httpRequest.Headers.Add("PAYDUNYA-PRIVATE-KEY", config.PrivateKey);
        httpRequest.Headers.Add("PAYDUNYA-PUBLIC-KEY", config.PublicKey);
        httpRequest.Headers.Add("PAYDUNYA-TOKEN", config.Token);

        HttpResponseMessage httpResponse;

        try
        {
            httpResponse = await client.SendAsync(httpRequest, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "PayDunya injoignable lors de la confirmation IPN ({Ref}).", providerTransactionRef);
            throw new PaymentProviderException("L'agrégateur de paiement est momentanément injoignable pour confirmer le paiement.");
        }

        var body = await httpResponse.Content.ReadFromJsonAsync<ConfirmInvoiceResponse>(cancellationToken)
            ?? throw new PaymentProviderException("Réponse de confirmation illisible de l'agrégateur de paiement.");

        // "completed" est le statut documenté par PayDunya pour une facture réellement payée. Tout le
        // reste (pending, cancelled, absent…) n'est PAS une confirmation — en cas de doute, on refuse.
        var isPaid = string.Equals(body.Status, "completed", StringComparison.OrdinalIgnoreCase);
        var amount = body.Invoice?.TotalAmount ?? 0m;

        return new PaymentConfirmationResult(isPaid, amount, body.Status ?? body.ResponseText ?? "inconnu");
    }

    private sealed record ConfirmInvoiceResponse(
        [property: JsonPropertyName("response_code")] string? ResponseCode,
        [property: JsonPropertyName("response_text")] string? ResponseText,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("invoice")] ConfirmInvoiceDetails? Invoice);

    private sealed record ConfirmInvoiceDetails(
        [property: JsonPropertyName("total_amount")] decimal? TotalAmount);

    private sealed record InvoiceDetails(
        [property: JsonPropertyName("total_amount")] decimal TotalAmount,
        [property: JsonPropertyName("description")] string Description);

    private sealed record StoreDetails([property: JsonPropertyName("name")] string Name);

    private sealed record InvoiceActions(
        [property: JsonPropertyName("cancel_url")] string CancelUrl,
        [property: JsonPropertyName("return_url")] string ReturnUrl,
        [property: JsonPropertyName("callback_url")] string CallbackUrl);

    private sealed record CreateInvoiceRequest(
        [property: JsonPropertyName("invoice")] InvoiceDetails Invoice,
        [property: JsonPropertyName("store")] StoreDetails Store,
        [property: JsonPropertyName("actions")] InvoiceActions Actions,
        [property: JsonPropertyName("custom_data")] Dictionary<string, string> CustomData);

    private sealed record CreateInvoiceResponse(
        [property: JsonPropertyName("response_code")] string ResponseCode,
        [property: JsonPropertyName("response_text")] string ResponseText,
        [property: JsonPropertyName("token")] string? Token);
}
