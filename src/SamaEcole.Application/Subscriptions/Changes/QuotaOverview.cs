using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Changes;

/// <summary>Consigne l'état de quota constaté (voir <see cref="OverQuotaTracker"/>) ; interface pour tester le garde sans base.</summary>
public interface IOverQuotaTracker
{
    Task RecordAsync(StudentQuotaStatus quota, CancellationToken cancellationToken);
}

/// <summary>
/// Pose et lève <see cref="TenantSubscription.OverQuotaSince"/> à chaque évaluation du quota (création d'élève, import, écran
/// du Directeur) : la date de PREMIER constat de dépassement n'est écrite qu'une fois, et effacée dès que l'école repasse sous
/// son plafond. Le Super Admin s'en sert pour relancer ; l'état réel, lui, se relit toujours d'après l'effectif.
///
/// N'écrit qu'aux TRANSITIONS (jamais à chaque appel), et seulement pour une souscription <c>Active</c> : une école encore à
/// l'Onboarding n'a pas de quota applicable.
/// </summary>
public class OverQuotaTracker(IApplicationDbContext dbContext, ITenantProvider tenantProvider, TimeProvider timeProvider)
    : IOverQuotaTracker
{
    public async Task RecordAsync(StudentQuotaStatus quota, CancellationToken cancellationToken)
    {
        if (quota.Denial == StudentAdmissionDenial.SubscriptionNotActive)
        {
            return;
        }

        var schoolId = tenantProvider.CurrentSchoolId;

        if (schoolId is null)
        {
            return;
        }

        var subscription = await dbContext.TenantSubscriptions.FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

        if (subscription is null)
        {
            return;
        }

        var over = quota.State != StudentQuotaState.WithinQuota;

        if (over && subscription.OverQuotaSince is null)
        {
            subscription.OverQuotaSince = timeProvider.GetUtcNow();
        }
        else if (!over && subscription.OverQuotaSince is not null)
        {
            subscription.OverQuotaSince = null;
        }
        else
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// GET /tenant-subscription/quota — la position de l'école par rapport à son quota, pour le bandeau du tableau de bord :
/// effectif, plafond, tolérance, état, tranche suggérée (celle qui contiendrait l'effectif) et demande en cours.
/// Évaluer le quota ici met aussi à jour <see cref="TenantSubscription.OverQuotaSince"/>.
/// </summary>
public record GetQuotaOverviewQuery : IRequest<QuotaOverviewDto?>;

public class GetQuotaOverviewQueryHandler(
    ITenantSubscriptionService subscriptionService,
    IApplicationDbContext dbContext,
    IOverQuotaTracker tracker)
    : IRequestHandler<GetQuotaOverviewQuery, QuotaOverviewDto?>
{
    public async Task<QuotaOverviewDto?> Handle(GetQuotaOverviewQuery request, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionService.GetCurrentAsync(cancellationToken);

        if (subscription is null || subscription.Status != nameof(TenantSubscriptionStatus.Active))
        {
            return null;
        }

        var quota = await subscriptionService.GetQuotaStatusAsync(cancellationToken);
        await tracker.RecordAsync(quota, cancellationToken);

        var tier = Enum.Parse<StudentQuotaTier>(subscription.StudentQuotaTier);
        var state = quota.State;

        StudentQuotaTier? suggested = null;

        if (state != StudentQuotaState.WithinQuota)
        {
            // La tranche qui contient l'effectif ; si c'est la tranche actuelle (tolérance seule entamée), la suivante.
            var minimum = QuotaTierResolver.MinimumTierFor(quota.CurrentStudentCount);
            var ladder = QuotaTierResolver.Ladder;
            var next = QuotaTierResolver.Rank(tier) + 1;

            suggested = QuotaTierResolver.Rank(minimum) > QuotaTierResolver.Rank(tier)
                ? minimum
                : next < ladder.Length ? ladder[next] : null;

            // Jamais la tranche sur mesure : elle se négocie, elle ne se propose pas. « Grand Groupe » (800) se demande.
            if (suggested == StudentQuotaTier.Tier4_Custom)
            {
                suggested = StudentQuotaTier.Tier3_800;
            }

            if (suggested == tier)
            {
                suggested = null;
            }
        }

        var overSince = await dbContext.TenantSubscriptions.AsNoTracking()
            .Select(s => s.OverQuotaSince)
            .FirstOrDefaultAsync(cancellationToken);

        var pendingId = await dbContext.SubscriptionChangeRequests.AsNoTracking()
            .Where(r => r.Status == SubscriptionChangeStatus.Pending)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new QuotaOverviewDto(
            quota.CurrentStudentCount,
            quota.MaxStudentLimit,
            quota.SoftQuotaLimit,
            state.ToString(),
            tier.ToString(),
            SubscriptionChangePlanner.TierLabel(tier),
            suggested?.ToString(),
            suggested is { } s ? SubscriptionChangePlanner.TierLabel(s) : null,
            overSince,
            pendingId);
    }
}
