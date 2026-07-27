using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Lit les accusés de réception au format Infobip — le même agrégateur que celui visé par défaut à
/// l'envoi (<see cref="HttpSmsService"/>) :
///
///   { "results": [ { "messageId": "…", "status": { "groupName": "DELIVERED" } } ] }
///
/// Authentification par HMAC-SHA256 du CORPS BRUT avec le secret partagé, comparé à l'en-tête de
/// signature. Contrairement au hash statique de PayDunya (une preuve de connaissance de la clé), une
/// signature calculée sur le corps lie l'autorisation à CE contenu précis : un corps rejoué avec des
/// identifiants modifiés ne passe pas.
///
/// Changer d'agrégateur = un autre adaptateur derrière la même interface, exactement comme à l'envoi.
/// </summary>
public class HmacSmsDeliveryReceiptReader(
    IOptions<SmsOptions> options,
    ILogger<HmacSmsDeliveryReceiptReader> logger) : ISmsDeliveryReceiptReader
{
    private readonly SmsOptions _options = options.Value;

    public string ProviderName => _options.Provider;

    public bool TryRead(
        string rawBody,
        string? signatureHeader,
        out IReadOnlyList<SmsDeliveryReceipt> receipts)
    {
        receipts = [];

        if (!IsSignatureValid(rawBody, signatureHeader))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);

            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<SmsDeliveryReceipt>();

            foreach (var entry in results.EnumerateArray())
            {
                if (!entry.TryGetProperty("messageId", out var messageId)
                    || messageId.GetString() is not { Length: > 0 } id)
                {
                    continue;
                }

                var groupName = entry.TryGetProperty("status", out var status)
                                && status.TryGetProperty("groupName", out var group)
                    ? group.GetString()
                    : null;

                // Seul DELIVERED vaut confirmation. PENDING est un état intermédiaire dont l'agrégateur
                // enverra la suite : le traiter comme un échec recréditerait l'école et marquerait
                // « échoué » un SMS encore en cours d'acheminement.
                if (string.Equals(groupName, "PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isDelivered = string.Equals(groupName, "DELIVERED", StringComparison.OrdinalIgnoreCase);

                parsed.Add(new SmsDeliveryReceipt(
                    id,
                    isDelivered,
                    isDelivered ? null : $"Non remis par l'opérateur ({groupName ?? "motif inconnu"})."));
            }

            receipts = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Accusé de réception SMS illisible reçu de {Provider}.", ProviderName);
            return false;
        }
    }

    /// <summary>
    /// Échec SÛR par défaut : sans secret configuré, aucune signature ne peut correspondre, et le
    /// webhook est donc fermé — plutôt que d'accepter tout le monde faute de configuration.
    /// </summary>
    private bool IsSignatureValid(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !_options.IsWebhookSecretConfigured)
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(_options.WebhookSecret),
                Encoding.UTF8.GetBytes(rawBody)));

        // Certains agrégateurs préfixent la signature (« sha256=… ») : on ne compare que la valeur.
        var provided = signatureHeader.Trim();
        var separator = provided.IndexOf('=');

        if (separator >= 0)
        {
            provided = provided[(separator + 1)..];
        }

        // Comparaison à temps constant, même raison que dans PayDunyaPaymentService : une comparaison
        // naïve fuit, par son temps de réponse, combien de caractères ont déjà été devinés.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(expected));
    }
}
