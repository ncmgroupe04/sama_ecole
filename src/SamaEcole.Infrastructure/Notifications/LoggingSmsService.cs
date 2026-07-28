using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Adaptateur Development : journalise le SMS au lieu de l'envoyer, comme LoggingEmailSender. Ne pas
/// confondre « message journalisé » et « message délivré ».
///
/// Renvoie tout de même un succès, segments compris : le développement doit voir le solde se
/// décrémenter et l'historique se remplir exactement comme en production, sinon ces deux mécanismes
/// ne seraient jamais éprouvés avant la mise en ligne.
/// </summary>
public class LoggingSmsService(ILogger<LoggingSmsService> logger) : ISmsService
{
    public string ProviderName => "Logging";

    public Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken)
    {
        var segments = SmsSegments.Count(request.Body);

        logger.LogWarning(
            "SMS NON ENVOYÉ (mode développement).\nÀ : {To}\nSegments : {Segments}\n{Body}",
            request.To, segments, request.Body);

        return Task.FromResult(new SmsSendResult(
            IsSent: true,
            ProviderMessageId: $"dev-{Guid.CreateVersion7()}",
            FailureReason: null,
            SegmentCount: segments));
    }
}
