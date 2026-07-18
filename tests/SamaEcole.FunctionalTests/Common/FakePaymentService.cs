using System.Collections.Concurrent;
using System.Text.Json;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.FunctionalTests.Common;

/// <summary>
/// Remplace PayDunyaPaymentService dans les tests fonctionnels (tickets JGK-I05/I06) : aucune clé
/// PayDunya réelle n'existe pour cette suite, et un test fonctionnel n'a pas à dépendre d'un agrégateur
/// externe. Capture les requêtes reçues pour vérifier ce que le Handler lui a transmis (montant calculé
/// serveur, corrélation) sans jamais toucher le réseau.
///
/// Le FORMAT exact d'un webhook PayDunya réel est vérifié séparément par PayDunyaPaymentServiceTests
/// (tests unitaires, contre un transport HTTP simulé) — ce faux service, lui, accepte un JSON SIMPLIFIÉ
/// ({ "internalPaymentId": "...", "signatureValid": true|false }) pour que chaque test exprime le
/// scénario qu'il veut exercer directement dans le corps qu'il poste, sans état partagé mutable entre
/// scénarios (donc sans dépendance à l'ordre d'exécution des tests).
/// </summary>
public class FakePaymentService : IPaymentService
{
    private readonly ConcurrentQueue<PaymentInitiationRequest> _received = new();

    public string ProviderName => "FakeProvider";

    /// <summary>Si renseigné, le prochain appel lève une PaymentProviderException avec ce message (simule un refus PayDunya).</summary>
    public string? FailureMessage { get; set; }

    public IReadOnlyCollection<PaymentInitiationRequest> Received => _received.ToArray();

    public Task<PaymentInitiationResult> InitiatePaymentAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken)
    {
        _received.Enqueue(request);

        if (FailureMessage is not null)
        {
            throw new PaymentProviderException(FailureMessage);
        }

        var reference = $"FAKE-{Guid.NewGuid():N}";
        return Task.FromResult(new PaymentInitiationResult(reference, $"https://fake-checkout.example/{reference}"));
    }

    public WebhookParseResult ParseWebhook(string rawBody, string? contentType)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            var signatureValid = !root.TryGetProperty("signatureValid", out var signatureProperty)
                || signatureProperty.GetBoolean();

            Guid? internalPaymentId = root.TryGetProperty("internalPaymentId", out var idProperty)
                && Guid.TryParse(idProperty.GetString(), out var parsed)
                ? parsed
                : null;

            return new WebhookParseResult(signatureValid, internalPaymentId, rawBody);
        }
        catch (JsonException)
        {
            return new WebhookParseResult(false, null, rawBody);
        }
    }

    /// <summary>Contrôle la réponse du PROCHAIN appel à ConfirmPaymentAsync (simule l'appel IPN, ticket JGK-I06).</summary>
    public bool NextConfirmationIsPaid { get; set; } = true;

    public decimal NextConfirmationAmount { get; set; }

    public Task<PaymentConfirmationResult> ConfirmPaymentAsync(string providerTransactionRef, CancellationToken cancellationToken)
        => Task.FromResult(new PaymentConfirmationResult(
            NextConfirmationIsPaid, NextConfirmationAmount, NextConfirmationIsPaid ? "completed" : "failed"));

    public void Clear()
    {
        _received.Clear();
        FailureMessage = null;
        NextConfirmationIsPaid = true;
        NextConfirmationAmount = 0;
    }
}
