using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Notifications.Commands.ProcessSmsDeliveryReceipt;

/// <summary>
/// POST /webhooks/sms/{provider} — accusé de réception (DLR) de l'agrégateur : c'est LUI qui fait
/// passer un SMS de « remis au fournisseur » (Sent) à « reçu par le parent » (Delivered).
///
/// Sans ce retour, l'école ne saurait jamais si son alerte est arrivée : un agrégateur accepte un
/// envoi bien avant de savoir si le numéro existe, si le téléphone est éteint, ou si l'opérateur l'a
/// rejeté. Un accusé NÉGATIF recrédite les segments — l'école ne paie pas un SMS jamais reçu.
///
/// Route PUBLIQUE : l'appelant est l'agrégateur, sans JWT. Toute la sécurité tient donc dans la
/// signature (docs/Volume_7_Security.md §12bis), vérifiée AVANT le moindre accès à la base.
/// </summary>
public record ProcessSmsDeliveryReceiptCommand(
    string Provider,
    string RawBody,
    string? SignatureHeader) : IRequest<SmsDeliveryReceiptResult>;

/// <param name="AppliedCount">Accusés effectivement rapprochés d'un message connu.</param>
/// <param name="IgnoredCount">
/// Accusés sans correspondance : rejeu d'un accusé déjà traité, ou message inconnu de nous. Ni l'un
/// ni l'autre n'est une erreur — répondre autre chose que 200 pousserait l'agrégateur à réémettre
/// indéfiniment un accusé que nous refusons de toute façon.
/// </param>
public record SmsDeliveryReceiptResult(int AppliedCount, int IgnoredCount);

public class ProcessSmsDeliveryReceiptCommandHandler(
    IEnumerable<ISmsDeliveryReceiptReader> readers,
    ISmsQueueStore queueStore,
    ILogger<ProcessSmsDeliveryReceiptCommandHandler> logger)
    : IRequestHandler<ProcessSmsDeliveryReceiptCommand, SmsDeliveryReceiptResult>
{
    public async Task<SmsDeliveryReceiptResult> Handle(
        ProcessSmsDeliveryReceiptCommand request, CancellationToken cancellationToken)
    {
        var reader = readers.FirstOrDefault(
            r => string.Equals(r.ProviderName, request.Provider, StringComparison.OrdinalIgnoreCase));

        if (reader is null)
        {
            // Agrégateur inconnu : traité comme une signature invalide, et non comme un 404. Un 404
            // confirmerait à un appelant anonyme quels fournisseurs sont configurés.
            throw new InvalidWebhookSignatureException("Accusé de réception SMS non authentifié.");
        }

        if (!reader.TryRead(request.RawBody, request.SignatureHeader, out var receipts))
        {
            logger.LogWarning(
                "Accusé de réception SMS rejeté pour {Provider} : signature absente ou invalide.",
                request.Provider);

            throw new InvalidWebhookSignatureException("Accusé de réception SMS non authentifié.");
        }

        var applied = 0;
        var ignored = 0;

        foreach (var receipt in receipts)
        {
            var matched = await queueStore.ApplyDeliveryReceiptAsync(
                receipt.ProviderMessageId, receipt.IsDelivered, receipt.FailureReason, cancellationToken);

            if (matched)
            {
                applied++;
            }
            else
            {
                ignored++;
            }
        }

        logger.LogInformation(
            "Accusés de réception SMS de {Provider} : {Applied} appliqué(s), {Ignored} sans correspondance.",
            request.Provider, applied, ignored);

        return new SmsDeliveryReceiptResult(applied, ignored);
    }
}
