using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Application.Subscriptions.Pricing;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Changes;

/// <param name="Subscription">La souscription de l'école (suivie : la création d'une demande n'y touche pas).</param>
public sealed record ChangeEvaluation(
    SubscriptionState Current,
    ChangePlan Plan,
    SubscriptionChangePreview Preview,
    TenantSubscription Subscription);

/// <summary>
/// Évalue un changement d'abonnement pour l'école COURANTE (celle du JWT — AGENTS.md règle #10) : état actuel, effectif,
/// classes par cycle, tarifs, demande déjà en attente. C'est le point d'entrée UNIQUE du verrouillage : les trois
/// commandes qui modifiaient librement profil, cycles et modules passent par <see cref="EnsureNoChangeAsync"/>, et la
/// création d'une demande réutilise la même évaluation — l'aperçu montré au Directeur est donc, par construction, ce que
/// le Super Admin voit et ce que la validation écrit.
/// </summary>
public class SubscriptionChangeService(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    SubscriptionPricingEngine pricingEngine)
{
    public const string UpgradeMessage =
        "Pour basculer vers ce profil ou ajouter ces options/effectifs, une mise à jour de votre abonnement est nécessaire (tarif ajusté).";

    public const string DowngradeMessage =
        "Retirer un cycle ou une option, ou baisser de tranche, est traité par notre équipe pour protéger vos classes et vos élèves : "
        + "envoyez une demande, le support vous répond.";

    /// <summary>
    /// Évalue <paramref name="change"/>. <c>null</c> si l'école n'a pas de souscription, ou si elle est encore à l'Onboarding
    /// (son profil et sa tranche se choisissent alors librement) : il n'y a rien à verrouiller.
    /// </summary>
    public async Task<ChangeEvaluation?> EvaluateAsync(SubscriptionChangeSet change, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var subscription = await dbContext.TenantSubscriptions.FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

        if (subscription is null || subscription.Status == TenantSubscriptionStatus.PendingOnboarding)
        {
            return null;
        }

        var settings = await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        // Students et Classrooms portent déjà le Global Query Filter (tenant + non supprimés).
        var studentCount = await dbContext.Students.AsNoTracking().CountAsync(cancellationToken);
        var classrooms = await dbContext.Classrooms.AsNoTracking()
            .GroupBy(c => c.Cycle)
            .Select(g => new { Cycle = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Cycle, x => x.Count, cancellationToken);

        var current = new SubscriptionState(
            subscription.ProfileType,
            subscription.StudentQuotaTier,
            ManagedCycleSet.FromStored(settings?.ManagedCycles ?? SchoolSettingsDefaults.ManagedCycles),
            subscription.HasBoarding,
            new ModulePreset(
                subscription.IsPedagogyEnabled, subscription.IsFinanceEnabled,
                subscription.IsInternatEnabled, subscription.IsCoranModuleEnabled));

        var plan = SubscriptionChangePlanner.Plan(current, change, studentCount, classrooms);

        var hasPending = await dbContext.SubscriptionChangeRequests.AsNoTracking()
            .AnyAsync(r => r.Status == SubscriptionChangeStatus.Pending, cancellationToken);

        var preview = await BuildPreviewAsync(schoolId, current, plan, hasPending, change, cancellationToken);

        return new ChangeEvaluation(current, plan, preview, subscription);
    }

    /// <summary>
    /// Le verrou. Ne fait rien si la modification demandée ne change pas l'abonnement (ou si l'école n'en a pas à
    /// protéger) ; sinon refuse avec l'aperçu du tarif ajusté — <see cref="SubscriptionChangeRequiredException"/>.
    /// </summary>
    public async Task EnsureNoChangeAsync(SubscriptionChangeSet change, CancellationToken cancellationToken)
    {
        var evaluation = await EvaluateAsync(change, cancellationToken);

        if (evaluation is { Plan.IsNoOp: false })
        {
            throw new SubscriptionChangeRequiredException(evaluation.Preview);
        }
    }

    private async Task<SubscriptionChangePreview> BuildPreviewAsync(
        Guid schoolId,
        SubscriptionState current,
        ChangePlan plan,
        bool hasPendingRequest,
        SubscriptionChangeSet change,
        CancellationToken cancellationToken)
    {
        // Le tarif ACTUEL n'est connu que si l'école est facturée d'après la grille par tranche : une école qui suit encore
        // l'ancienne grille (cycles × taille) n'a pas de tarif comparable, et l'écart se chiffre avec le support.
        var billedByTier = await pricingEngine.IsBilledByTierAsync(schoolId, cancellationToken);

        var currentQuote = !billedByTier || QuotaTierResolver.RequiresQuote(current.Tier)
            ? new SubscriptionPriceQuote(null, null, QuotaTierResolver.RequiresQuote(current.Tier), Known: false)
            : ToQuote(await pricingEngine.QuoteAsync(current.Tier, current.HasBoarding, cancellationToken));

        var targetQuote = ToQuote(await pricingEngine.QuoteAsync(plan.Target.Tier, plan.Target.HasBoarding, cancellationToken));

        var comparable = currentQuote.Known && targetQuote.Known && !targetQuote.RequiresQuote;

        return new SubscriptionChangePreview(
            HasChanges: !plan.IsNoOp,
            IsDowngrade: plan.IsDowngrade,
            Message: plan.IsDowngrade ? DowngradeMessage : UpgradeMessage,
            Changes: plan.Lines,
            Problems: plan.Problems.Select(p => p.Message).ToList(),
            Current: currentQuote,
            Target: targetQuote,
            DifferenceMonthlyXof: comparable ? targetQuote.MonthlyXof - currentQuote.MonthlyXof : null,
            DifferenceAnnualXof: comparable ? targetQuote.AnnualXof - currentQuote.AnnualXof : null,
            HasPendingRequest: hasPendingRequest,
            Request: change);
    }

    private static SubscriptionPriceQuote ToQuote(PricingQuote quote) =>
        new(quote.MonthlyXof, quote.AnnualXof, quote.RequiresQuote, Known: !quote.RequiresQuote);
}
