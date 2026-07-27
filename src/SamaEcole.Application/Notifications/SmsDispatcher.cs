using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Notifications;

/// <summary>
/// Point de passage UNIQUE de tout SMS sortant. Chaque déclencheur (assiduité, impayé, reçu) décrit
/// seulement QUOI envoyer et À QUI ; les quatre gardes ci-dessous sont appliquées ici, une fois pour
/// toutes, plutôt que recopiées dans chaque appelant où l'une finirait tôt ou tard par manquer :
///
///   1. FORMULE — le SMS est une option Premium (<see cref="Feature.SmsNotifications"/>). Vérifiée
///      ici EN PLUS de [RequireFeature] côté API : les envois automatiques naissent d'un événement
///      métier (une saisie de retard), pas d'un appel HTTP, et ne traversent donc aucun endpoint.
///   2. COMMUTATEUR — l'école a-t-elle activé CE type d'alerte (SchoolSettings) ?
///   3. SOLDE — débité atomiquement, coût en segments (voir la remarque sur ExecuteUpdateAsync).
///   4. HISTORIQUE — une ligne pour chaque tentative, y compris les refus, afin que l'école puisse
///      expliquer sa consommation comme le silence d'une alerte attendue.
///
/// NE LÈVE JAMAIS : un SMS est un effet de bord d'une opération métier (saisir un retard, encaisser
/// un paiement). Faire échouer l'opération parce que l'opérateur est indisponible serait une
/// régression bien pire que l'alerte manquée.
/// </summary>
public class SmsDispatcher(
    IApplicationDbContext dbContext,
    ISmsService smsService,
    TimeProvider timeProvider,
    ILogger<SmsDispatcher> logger) : ISmsDispatcher
{
    public async Task<SmsDispatchOutcome> DispatchAsync(
        SmsDispatchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Recipient))
        {
            return SmsDispatchOutcome.Skipped("Aucun numéro de téléphone renseigné.");
        }

        // IgnoreQueryFilters, pour la MÊME raison que SchoolSettings plus bas : Subscription est une
        // ITenantEntity, mais ce code s'exécute souvent depuis un gestionnaire d'événement dont le
        // tenant ambiant n'est pas nécessairement celui de l'événement. On filtre donc explicitement
        // sur le SchoolId porté par l'événement, seule valeur digne de confiance ici. La policy RLS
        // borne de toute façon la lecture à l'école de la session en cours.
        var plan = await dbContext.Subscriptions
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == request.SchoolId && !s.IsDeleted)
            .Select(s => (SubscriptionPlan?)s.Plan)
            .SingleOrDefaultAsync(cancellationToken);

        if (plan is not { } currentPlan || !PlanFeatures.Includes(currentPlan, Feature.SmsNotifications))
        {
            return SmsDispatchOutcome.Skipped("Les SMS ne sont pas inclus dans la formule de cet établissement.");
        }

        // IgnoreQueryFilters : ce code s'exécute souvent depuis un gestionnaire d'événement, dont le
        // tenant est celui de la requête d'origine — on filtre donc explicitement sur le SchoolId
        // porté par l'événement, seule valeur digne de confiance ici. La policy RLS borne de toute
        // façon la lecture à l'école de la session en cours.
        var settings = await dbContext.SchoolSettings
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.SchoolId == request.SchoolId && !s.IsDeleted, cancellationToken);

        if (settings is null || !IsTriggerEnabled(settings, request.Trigger))
        {
            return SmsDispatchOutcome.Skipped("Ce type d'alerte SMS est désactivé pour cet établissement.");
        }

        var cost = SmsSegments.Count(request.Body);

        // Débit ATOMIQUE : la condition sur le solde et la soustraction sont un seul UPDATE, exécuté
        // par PostgreSQL. Un lire-modifier-écrire côté application perdrait un débit dès que deux
        // alertes partent en même temps pour la même école (AGENTS.md règle #5, même esprit que le
        // verrou optimiste). Zéro ligne affectée = solde insuffisant.
        var debited = await dbContext.SchoolSettings
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == request.SchoolId && s.SmsCreditBalance >= cost)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(s => s.SmsCreditBalance, s => s.SmsCreditBalance - cost),
                cancellationToken);

        if (debited == 0)
        {
            await RecordAsync(request, SmsDeliveryStatus.InsufficientCredit, null,
                "Solde SMS épuisé.", cost, cancellationToken);

            logger.LogWarning(
                "SMS non envoyé pour l'établissement {SchoolId} : solde insuffisant ({Cost} segment(s) requis).",
                request.SchoolId, cost);

            return SmsDispatchOutcome.Skipped("Solde SMS épuisé.");
        }

        var result = await smsService.SendAsync(new SmsSendRequest(request.Recipient, request.Body), cancellationToken);

        await RecordAsync(
            request,
            result.IsSent ? SmsDeliveryStatus.Sent : SmsDeliveryStatus.Failed,
            result.ProviderMessageId,
            result.FailureReason,
            result.SegmentCount,
            cancellationToken);

        // Échec de l'opérateur : le solde est RECRÉDITÉ. L'école ne doit pas payer un segment que
        // l'agrégateur a refusé — l'historique, lui, conserve la trace de la tentative.
        if (!result.IsSent)
        {
            await dbContext.SchoolSettings
                .IgnoreQueryFilters()
                .Where(s => s.SchoolId == request.SchoolId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(s => s.SmsCreditBalance, s => s.SmsCreditBalance + cost),
                    cancellationToken);

            return SmsDispatchOutcome.Failed(result.FailureReason ?? "Envoi refusé par l'opérateur.");
        }

        return SmsDispatchOutcome.Sent;
    }

    private static bool IsTriggerEnabled(SchoolSettings settings, SmsTrigger trigger) => trigger switch
    {
        SmsTrigger.AttendanceAlert => settings.SmsOnAttendanceAlert,
        SmsTrigger.DuesReminder => settings.SmsOnDuesReminder,
        SmsTrigger.PaymentReceipt => settings.SmsOnPaymentReceipt,

        // Manuel : l'utilisateur l'a explicitement demandé, aucun commutateur à consulter.
        _ => true
    };

    private async Task RecordAsync(
        SmsDispatchRequest request,
        SmsDeliveryStatus status,
        string? providerMessageId,
        string? failureReason,
        int segmentCount,
        CancellationToken cancellationToken)
    {
        dbContext.SmsMessages.Add(new SmsMessage
        {
            SchoolId = request.SchoolId,
            Recipient = request.Recipient,
            Body = request.Body,
            Trigger = request.Trigger,
            Status = status,
            ProviderMessageId = providerMessageId,
            FailureReason = failureReason,
            SegmentCount = segmentCount,
            StudentId = request.StudentId,
            SentAt = timeProvider.GetUtcNow()
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
