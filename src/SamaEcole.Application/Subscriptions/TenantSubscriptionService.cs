using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Implémentation en lecture seule de <see cref="ITenantSubscriptionService"/>. L'école est celle du JWT
/// (<see cref="ITenantProvider"/>) ; le Global Query Filter et la RLS bornent de toute façon la lecture à
/// ce tenant — le filtre explicite sur SchoolId n'est qu'une ceinture de plus.
/// </summary>
public class TenantSubscriptionService(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : ITenantSubscriptionService
{
    public async Task<TenantSubscriptionDto?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(cancellationToken);

        return subscription is null ? null : ToDto(subscription);
    }

    public async Task<bool> IsModuleEnabledAsync(SchoolModule module, CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(cancellationToken);

        return subscription is not null && module switch
        {
            SchoolModule.Pedagogy => subscription.IsPedagogyEnabled,
            SchoolModule.Finance => subscription.IsFinanceEnabled,
            SchoolModule.Internat => subscription.IsInternatEnabled,
            SchoolModule.Coran => subscription.IsCoranModuleEnabled,
            _ => false
        };
    }

    public async Task<StudentQuotaStatus> GetQuotaStatusAsync(CancellationToken cancellationToken)
    {
        var subscription = await LoadAsync(cancellationToken);

        // Students porte déjà le Global Query Filter (tenant + non supprimés) : on compte les élèves vivants.
        var currentCount = await dbContext.Students.AsNoTracking().CountAsync(cancellationToken);

        return subscription is null
            ? StudentQuotaEvaluator.WithoutSubscription(currentCount)
            : StudentQuotaEvaluator.Evaluate(
                currentCount, subscription.MaxStudentLimit, subscription.SoftQuotaLimit, subscription.Status);
    }

    private async Task<TenantSubscription?> LoadAsync(CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        return await dbContext.TenantSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
    }

    private static TenantSubscriptionDto ToDto(TenantSubscription s) => new(
        s.Id,
        s.SchoolId,
        s.ProfileType.ToString(),
        s.StudentQuotaTier.ToString(),
        s.MaxStudentLimit,
        s.SoftQuotaLimit,
        s.Status.ToString(),
        s.IsPedagogyEnabled,
        s.IsFinanceEnabled,
        s.IsInternatEnabled,
        s.IsCoranModuleEnabled);
}
