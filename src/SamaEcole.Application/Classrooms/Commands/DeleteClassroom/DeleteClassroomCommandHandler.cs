using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
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

        // Règle métier obligatoire : une classe encore liée à des élèves ACTIFS ne peut pas être
        // archivée (elle disparaîtrait de tous les sélecteurs, rendant ces fiches orphelines de
        // classe active).
        //
        // `!s.IsDeleted` est écrit ICI explicitement, en plus du Global Query Filter qui le pose
        // déjà : une fiche élève ARCHIVÉE ne doit jamais bloquer l'archivage de sa classe — c'est
        // exactement le faux positif remonté du terrain (« l'élève a été supprimé mais l'API dit
        // qu'il est encore rattaché »). Le rendre visible au callsite garde la règle lisible et la
        // met à l'abri d'un éventuel `.IgnoreQueryFilters()` ajouté un jour à cette requête.
        var attachedStudentCount = await dbContext.Students
            .CountAsync(s => s.ClassroomId == request.Id && !s.IsDeleted, cancellationToken);

        if (attachedStudentCount > 0)
        {
            var studentPhrase = attachedStudentCount > 1
                ? $"{attachedStudentCount} élèves y sont encore rattachés"
                : "1 élève y est encore rattaché";

            throw new BusinessRuleException(
                $"Impossible de supprimer la classe « {classroom.Name} » : {studentPhrase}. "
                + "Transférez ces élèves vers une autre classe ou supprimez leur fiche, puis réessayez.");
        }

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
