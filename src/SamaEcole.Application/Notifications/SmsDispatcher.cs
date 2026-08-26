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
/// N'APPELLE PAS le fournisseur : il INSCRIT le message en file (statut Pending) et rend la main.
/// SmsQueueProcessor s'occupe de la remise, avec report et nouvelles tentatives. Trois raisons :
/// une relance d'impayés ne tient plus la requête HTTP ouverte pendant des centaines d'allers-retours
/// réseau ; une panne de l'agrégateur n'est plus une alerte perdue mais un envoi différé ; et un
/// événement métier (saisie d'un retard) n'attend plus un service tiers pour se conclure.
///
/// Le SOLDE est débité DÈS LA MISE EN FILE, pas à la remise : sinon une relance de masse accepterait
/// mille messages sur un solde de cent, et l'école découvrirait le refus neuf cents SMS plus tard.
/// Le worker recrédite ce qui n'a finalement pas pu partir.
///
/// NE LÈVE JAMAIS : un SMS est un effet de bord d'une opération métier (saisir un retard, encaisser
/// un paiement). Faire échouer l'opération parce que l'opérateur est indisponible serait une
/// régression bien pire que l'alerte manquée.
/// </summary>
public class SmsDispatcher(
    IApplicationDbContext dbContext,
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
            var refusedId = await RecordAsync(request, SmsDeliveryStatus.InsufficientCredit,
                "Solde SMS épuisé.", cost, cancellationToken);

            logger.LogWarning(
                "SMS non envoyé pour l'établissement {SchoolId} : solde insuffisant ({Cost} segment(s) requis).",
                request.SchoolId, cost);

            return SmsDispatchOutcome.Skipped("Solde SMS épuisé.", refusedId);
        }

        // Éligible IMMÉDIATEMENT (NextAttemptAt = maintenant) : le worker le prendra à son prochain
        // tour. Aucun appel réseau ici — c'est tout l'objet de la file.
        var messageId = await RecordAsync(request, SmsDeliveryStatus.Pending, null, cost, cancellationToken);

        return SmsDispatchOutcome.Queued(messageId);
    }

    private static bool IsTriggerEnabled(SchoolSettings settings, SmsTrigger trigger) => trigger switch
    {
        SmsTrigger.AttendanceAlert => settings.SmsOnAttendanceAlert,
        SmsTrigger.DuesReminder => settings.SmsOnDuesReminder,
        SmsTrigger.PaymentReceipt => settings.SmsOnPaymentReceipt,

        // Bulletin et envoi manuel : DÉLIBÉRÉMENT sans commutateur. Les trois valeurs ci-dessus sont
        // des alertes AUTOMATIQUES, qu'une école doit pouvoir couper sans rien décider au cas par
        // cas. Ces deux-ci naissent au contraire d'un geste explicite (« envoyer le bulletin ») :
        // un commutateur qui les annulerait en silence rendrait le bouton menteur.
        SmsTrigger.ReportCard or SmsTrigger.Manual or SmsTrigger.ExamConvocation => true,

        _ => true
    };

    private async Task<Guid> RecordAsync(
        SmsDispatchRequest request,
        SmsDeliveryStatus status,
        string? failureReason,
        int segmentCount,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var message = new SmsMessage
        {
            SchoolId = request.SchoolId,
            Recipient = request.Recipient!,
            Body = request.Body,
            Trigger = request.Trigger,
            Status = status,
            FailureReason = failureReason,
            SegmentCount = segmentCount,
            StudentId = request.StudentId,
            SentAt = now,

            // Seul un message EN FILE est éligible à une remise. Un refus pour solde épuisé n'est pas
            // à retenter : il est écrit dans l'historique et s'arrête là, d'où le null.
            NextAttemptAt = status == SmsDeliveryStatus.Pending ? now : null
        };

        dbContext.SmsMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        return message.Id;
    }
}
