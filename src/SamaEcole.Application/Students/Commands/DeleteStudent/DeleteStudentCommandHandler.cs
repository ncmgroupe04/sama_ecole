using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Students.Deletion;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Commands.DeleteStudent;

public class DeleteStudentCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    IKpiCacheService kpiCache)
    : IRequestHandler<DeleteStudentCommand, Unit>
{
    public async Task<Unit> Handle(DeleteStudentCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser un élève d'une
        // autre école renvoie 404, jamais une suppression silencieuse.
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Un paiement actif la refuse (texte imposé) ; tout
        // autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkStudentDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, StudentDeletionPolicy.Policy, request.Id, cancellationToken);

        // Même verrou optimiste que UpdateStudentCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(student, request.RowVersion);

        student.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        // L'effectif des classes et le quota d'élèves ont changé.
        kpiCache.Invalidate(KpiCacheKeys.DirectorDashboard);

        return Unit.Value;
    }
}
