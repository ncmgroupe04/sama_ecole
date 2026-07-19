using System.Collections.Concurrent;
using System.Text.Json;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Doublure de <see cref="PayDunyaPaymentService"/> pour le développement local sans compte marchand
/// PayDunya. Enregistrée par DependencyInjection UNIQUEMENT quand Environment=Development ET
/// PayDunyaOptions.IsConfigured est faux (jamais en Staging/Production, quelle que soit la
/// configuration — la condition vit dans DependencyInjection, pas ici).
///
/// Ne simule PAS la confirmation en écrivant Confirmed directement : elle simule l'AGRÉGATEUR
/// lui-même. RedirectUrl pointe vers une page locale (DevPaymentSimulationController) qui poste le
/// même webhook public (POST /api/v1/webhooks/payments/{provider}) qu'un vrai callback PayDunya —
/// le paiement passe donc par EXACTEMENT le même chemin qu'en production
/// (ProcessPaymentWebhookHandler), AGENTS.md règle #11 reste vraie même ici.
///
/// Singleton (contrairement à PayDunyaPaymentService, Scoped) : InitiatePaymentAsync et le webhook
/// simulé arrivent dans deux requêtes HTTP distinctes ; seul un état partagé entre les deux permet à
/// ConfirmPaymentAsync de retrouver le montant exact à confirmer sans toucher la base de données.
/// </summary>
public class DevPaymentService : IPaymentService
{
    public readonly record struct PendingPayment(Guid InternalPaymentId, decimal Amount, string Description);

    private readonly ConcurrentDictionary<string, PendingPayment> _pending = new();

    public string ProviderName => "PayDunya";

    public Task<PaymentInitiationResult> InitiatePaymentAsync(
        PaymentInitiationRequest request, CancellationToken cancellationToken)
    {
        var reference = $"DEV-{Guid.NewGuid():N}";
        _pending[reference] = new PendingPayment(request.InternalPaymentId, request.Amount, request.Description);

        return Task.FromResult(new PaymentInitiationResult(reference, $"/dev/paydunya-checkout/{reference}"));
    }

    /// <summary>Lu par DevPaymentSimulationController pour afficher le montant/la description du faux guichet.</summary>
    public bool TryGetPending(string reference, out PendingPayment payment) => _pending.TryGetValue(reference, out payment);

    /// <summary>
    /// Même contrat simplifié que FakePaymentService (tests fonctionnels) :
    /// { "internalPaymentId": "...", "signatureValid": true } — posé par dev-paydunya-checkout.js,
    /// jamais par PayDunya (qui n'est jamais appelé depuis ce service).
    /// </summary>
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

    public Task<PaymentConfirmationResult> ConfirmPaymentAsync(
        string providerTransactionRef, CancellationToken cancellationToken)
        => Task.FromResult(_pending.TryGetValue(providerTransactionRef, out var payment)
            ? new PaymentConfirmationResult(true, payment.Amount, "completed (simulé — Dev)")
            : new PaymentConfirmationResult(false, 0m, "introuvable (Dev)"));
}
