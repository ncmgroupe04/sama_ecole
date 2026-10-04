using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.RestoreClassroom;

/// <summary>POST /api/v1/classrooms/{id}/restore — restaure une classe supprimée logiquement.</summary>
public record RestoreClassroomCommand(Guid Id) : IRequest<Unit>;

public class RestoreClassroomCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IKpiCacheService kpiCache)
    : IRequestHandler<RestoreClassroomCommand, Unit>
{
    public async Task<Unit> Handle(RestoreClassroomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Seule la classe revient : son programme (class_subjects) n'est pas réactivé implicitement
        // (aucune fusion de données, conception §3.2).
        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.Classrooms, schoolId, request.Id, "Une classe",
            c => other => other.Name == c.Name, beforeRestore: null, cancellationToken);

        // Le taux d'occupation du dashboard Directeur compte les classes actives (voir DeleteClassroomCommandHandler).
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);
        return Unit.Value;
    }
}
