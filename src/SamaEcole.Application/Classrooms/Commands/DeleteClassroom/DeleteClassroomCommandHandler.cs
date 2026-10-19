using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Classrooms.Deletion;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Classrooms.Commands.DeleteClassroom;

public class DeleteClassroomCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<DeleteClassroomCommand, Unit>
{
    public async Task<Unit> Handle(DeleteClassroomCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une classe d'une autre école renvoie 404, jamais une suppression silencieuse.
        var classroom = await dbContext.Classrooms
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkClassroomDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, ClassroomDeletionPolicy.Policy, request.Id, cancellationToken);

        // Même verrou optimiste que UpdateClassroomCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(classroom, request.RowVersion);

        classroom.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        // Le taux d'occupation du dashboard Directeur (GetDirectorDashboardQueryHandler) compte les
        // classes actives : sans invalidation il resterait figé jusqu'à 7 min (KpiCacheSettings.TtlMinutes)
        // après l'archivage d'une classe.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return Unit.Value;
    }
}
