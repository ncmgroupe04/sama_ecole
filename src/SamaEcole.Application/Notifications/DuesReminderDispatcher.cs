using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Notifications;

public enum DuesReminderChannel
{
    /// <summary>Rien n'est parti (hors formule, alerte désactivée, sans numéro, solde SMS épuisé).</summary>
    None,

    /// <summary>Mis en file SMS (la remise réelle est asynchrone, voir SmsQueueProcessor).</summary>
    Sms,

    /// <summary>Accepté par Meta (WhatsApp).</summary>
    WhatsApp,
}

public record DuesReminderRequest(
    Guid SchoolId,
    string? Recipient,
    Guid StudentId,
    string StudentName,
    decimal OverdueAmount,
    string SmsBody);

/// <param name="SmsMessageId">Ligne d'historique SMS, quand le canal est le SMS (ou un SMS refusé).</param>
public record DuesReminderOutcome(DuesReminderChannel Channel, string? Reason, Guid? SmsMessageId = null)
{
    public bool IsDelivered => Channel != DuesReminderChannel.None;
}

/// <summary>Point de passage unique d'une relance d'impayé : WhatsApp d'abord, SMS en repli.</summary>
public interface IDuesReminderDispatcher
{
    Task<DuesReminderOutcome> DispatchAsync(DuesReminderRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Relance d'un impayé par WHATSAPP, avec repli SMS. Mêmes gardes que le SMS — formule
/// (<see cref="Feature.SmsNotifications"/>, WhatsApp et SMS font partie de la même option Premium) et
/// commutateur « relance d'impayés » de l'école — appliquées AVANT toute tentative WhatsApp : une école
/// qui a coupé les relances, ou hors formule, n'en reçoit aucune, quel que soit le canal.
///
/// Le repli SMS est déclenché par TOUT résultat WhatsApp autre que « accepté » : mode simulation,
/// modèle de relance non configuré, numéro sans WhatsApp, jeton expiré, Meta injoignable. Une relance
/// ne doit jamais être perdue parce qu'un canal est indisponible. Un message accepté par Meta n'est
/// en revanche PAS doublé d'un SMS (la famille recevrait deux fois la même relance).
///
/// WhatsApp ne débite pas le solde SMS et n'a pas (encore) d'historique propre : seul le journal
/// applicatif garde la trace d'un envoi WhatsApp. NE LÈVE JAMAIS.
/// </summary>
public class DuesReminderDispatcher(
    IApplicationDbContext dbContext,
    IWhatsAppSender whatsAppSender,
    ISmsDispatcher smsDispatcher,
    ILogger<DuesReminderDispatcher> logger) : IDuesReminderDispatcher
{
    public async Task<DuesReminderOutcome> DispatchAsync(
        DuesReminderRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Recipient) && await IsAllowedAsync(request.SchoolId, cancellationToken))
        {
            var content = new WhatsAppTemplateContent(
                [request.StudentName, request.OverdueAmount.ToString("N0")],
                Kind: WhatsAppTemplateKind.DuesReminder);

            var result = await whatsAppSender.SendAsync(
                new WhatsAppMessage(request.Recipient, request.SmsBody, Template: content), cancellationToken);

            if (result.IsSent)
            {
                logger.LogInformation(
                    "Relance d'impayé envoyée par WhatsApp (école {SchoolId}, élève {StudentId}).",
                    request.SchoolId, request.StudentId);
                return new DuesReminderOutcome(DuesReminderChannel.WhatsApp, null);
            }

            if (result.IsFailed)
            {
                logger.LogWarning(
                    "Relance WhatsApp échouée (école {SchoolId}, élève {StudentId}, code Meta {MetaCode}) : "
                    + "{Reason} — repli sur le SMS.",
                    request.SchoolId, request.StudentId, result.MetaErrorCode, result.FailureReason);
            }
        }

        var sms = await smsDispatcher.DispatchAsync(
            new SmsDispatchRequest(
                request.SchoolId, request.Recipient, request.SmsBody, SmsTrigger.DuesReminder, request.StudentId),
            cancellationToken);

        return new DuesReminderOutcome(
            sms.IsQueued ? DuesReminderChannel.Sms : DuesReminderChannel.None, sms.Reason, sms.MessageId);
    }

    /// <summary>Mêmes conditions que SmsDispatcher (formule + commutateur), sans le solde SMS.</summary>
    private async Task<bool> IsAllowedAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + filtre explicite sur le SchoolId : même raison que SmsDispatcher
        // (le tenant ambiant n'est pas toujours celui de l'envoi) ; la RLS borne de toute façon.
        var plan = await dbContext.Subscriptions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && !s.IsDeleted)
            .Select(s => (SubscriptionPlan?)s.Plan)
            .SingleOrDefaultAsync(cancellationToken);

        if (plan is not { } currentPlan || !PlanFeatures.Includes(currentPlan, Feature.SmsNotifications))
        {
            return false;
        }

        return await dbContext.SchoolSettings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(s => s.SchoolId == schoolId && !s.IsDeleted && s.SmsOnDuesReminder, cancellationToken);
    }
}
