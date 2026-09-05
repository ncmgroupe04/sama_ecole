using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Subscriptions.Commands.ProcessPaymentWebhook;

/// <summary>
/// Ticket JGK-I06. Ordre STRICT des opérations, chacune une garde contre la précédente :
///
///   1. Vérifier la signature — AVANT tout le reste (docs/Volume_7_Security.md §12bis). Un corps non
///      signé n'obtient ni recherche en base ni appel réseau.
///   2. Chercher la ligne LOCALEMENT (lecture seule) — évite un appel à l'agrégateur pour une référence
///      inconnue ou déjà traitée (économie réseau, mais surtout : on n'interroge PayDunya qu'une fois
///      qu'on sait avoir quelque chose à faire de sa réponse).
///   3. Sortie idempotente immédiate si la ligne n'est plus Initiated (rejeu du webhook) — AUCUN appel
///      à l'agrégateur, AUCUNE ré-écriture.
///   4. Appel IPN serveur à serveur — c'est LUI, jamais le corps du webhook, qui décide du montant et du
///      statut réels (règle #11, AGENTS.md ; « aucune confiance dans le client »).
///   5. Transition ATOMIQUE gardée (ISubscriptionPaymentStore.ConfirmAsync) : si un autre appel a traité
///      la ligne entre l'étape 2 et maintenant, la transition n'a AUCUN effet (idempotence garantie côté
///      base, pas seulement côté application — voir le commentaire de la fonction SQL).
/// </summary>
public class ProcessPaymentWebhookHandler(
    IPaymentService paymentService,
    ISubscriptionPaymentStore paymentStore,
    IAuditLogStore auditLogStore,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<ProcessPaymentWebhookHandler> logger)
    : IRequestHandler<ProcessPaymentWebhookCommand, Unit>
{
    public async Task<Unit> Handle(ProcessPaymentWebhookCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Provider, paymentService.ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            // Ni une erreur de signature ni une référence inconnue : un agrégateur qu'on n'a jamais
            // configuré. 404, pas 401 — rien à authentifier pour un fournisseur qu'on ne reconnaît pas.
            throw new KeyNotFoundException($"Agrégateur de paiement '{request.Provider}' non pris en charge.");
        }

        var parsed = paymentService.ParseWebhook(request.RawBody, request.ContentType);

        if (!parsed.SignatureValid)
        {
            // Pas d'entrée dans audit_logs (table TENANT, exige un SchoolId de confiance) : à ce stade,
            // rien dans le corps n'est digne de confiance, y compris un éventuel SchoolId qu'il
            // contiendrait. Un log structuré suffit — même choix que pour un e-mail de connexion inconnu.
            logger.LogWarning(
                "Webhook {Provider} rejeté (signature invalide) depuis {IpAddress}.",
                request.Provider, request.RemoteIpAddress);
            throw new InvalidWebhookSignatureException("Signature de webhook invalide.");
        }

        if (parsed.InternalPaymentId is not { } internalPaymentId)
        {
            throw new KeyNotFoundException("Référence de paiement absente ou illisible dans le webhook.");
        }

        var lookup = await paymentStore.FindAsync(internalPaymentId, cancellationToken)
            ?? throw new KeyNotFoundException("Aucun paiement ne correspond à cette référence.");

        if (lookup.Status != SubscriptionPaymentStatus.Initiated)
        {
            // Rejeu (retry réseau de l'agrégateur) : critère explicite du ticket — ne JAMAIS retraiter.
            logger.LogInformation(
                "Webhook {Provider} : paiement {PaymentId} déjà au statut {Status}, aucun retraitement.",
                request.Provider, internalPaymentId, lookup.Status);
            return Unit.Value;
        }

        // Appel IPN — avec NOS clés, sur la référence que NOUS avons stockée à l'initiation (JGK-I05),
        // jamais une donnée relue depuis le corps du webhook (règle #11).
        var confirmation = await paymentService.ConfirmPaymentAsync(lookup.ProviderTransactionRef, cancellationToken);

        // Le montant RÉELLEMENT payé doit correspondre au montant calculé serveur à l'initiation — un
        // écart (paiement partiel, facture trafiquée) est un échec, jamais une confirmation partielle.
        var finalStatus = confirmation.IsPaid && confirmation.Amount == lookup.Amount
            ? SubscriptionPaymentStatus.Confirmed
            : SubscriptionPaymentStatus.Failed;

        var result = await paymentStore.ConfirmAsync(
            internalPaymentId, finalStatus, parsed.NormalizedPayloadJson, cancellationToken);

        if (result is null)
        {
            // Course perdue face à un appel concurrent (deux webhooks pour le même paiement, quasi
            // simultanés) : l'autre a déjà tranché entre l'étape de lecture et celle-ci. Idempotence
            // garantie par la garde SQL (WHERE Status = 'Initiated'), pas par ce contrôle applicatif.
            logger.LogInformation(
                "Webhook {Provider} : paiement {PaymentId} traité entre-temps par un appel concurrent.",
                request.Provider, internalPaymentId);
            return Unit.Value;
        }

        await auditLogStore.AppendAsync(
            result.SchoolId, result.DirectorUserId, "Subscriptions",
            finalStatus == SubscriptionPaymentStatus.Confirmed ? "ConfirmSubscriptionPayment" : "FailSubscriptionPayment",
            success: finalStatus == SubscriptionPaymentStatus.Confirmed,
            failureReason: finalStatus == SubscriptionPaymentStatus.Failed
                ? $"Statut agrégateur : {confirmation.RawStatus} — montant confirmé {confirmation.Amount} pour {lookup.Amount} attendu."
                : null,
            request.RemoteIpAddress, timeProvider.GetUtcNow(), cancellationToken);

        if (finalStatus == SubscriptionPaymentStatus.Confirmed)
        {
            // Le paiement est déjà confirmé en base (ConfirmAsync ci-dessus) : un échec d'ENVOI ne doit
            // pas faire échouer la réponse au webhook, sous peine de faire croire à l'agrégateur que le
            // traitement a échoué et de déclencher un rejeu inutile (l'idempotence de ConfirmAsync le
            // rendrait sans effet, mais autant ne pas le provoquer).
            try
            {
                await SendActivationEmailAsync(result, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Paiement {PaymentId} confirmé pour l'école {SchoolId}, mais l'e-mail d'activation n'a pas pu être envoyé.",
                    internalPaymentId, result.SchoolId);
            }
        }

        logger.LogInformation(
            "Webhook {Provider} : paiement {PaymentId} -> {Status} pour l'école {SchoolId}.",
            request.Provider, internalPaymentId, finalStatus, result.SchoolId);

        return Unit.Value;
    }

    private async Task SendActivationEmailAsync(SubscriptionPaymentConfirmation result, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {result.DirectorFullName},

             Votre paiement a été confirmé : l'abonnement de « {result.SchoolName} » est maintenant actif.

             Vous avez désormais accès à l'ensemble des modules de Unikol (élèves, classes, notes,
             finances…). Votre abonnement est valable jusqu'au {result.NewExpiresAt:dd/MM/yyyy}.
             """;

        await emailSender.SendAsync(
            new EmailMessage(result.DirectorEmail, "Votre abonnement Unikol est actif", body),
            cancellationToken);
    }
}
