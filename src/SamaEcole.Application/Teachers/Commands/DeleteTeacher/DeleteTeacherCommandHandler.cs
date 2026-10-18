using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Teachers.Deletion;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.DeleteTeacher;

public class DeleteTeacherCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteTeacherCommand, Unit>
{
    public async Task<Unit> Handle(DeleteTeacherCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // un enseignant d'une autre école renvoie 404, jamais une suppression silencieuse.
        var teacher = await dbContext.Teachers
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Enseignant {request.Id} introuvable.");

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkTeacherDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, TeacherDeletionPolicy.Policy, request.Id, cancellationToken);

        // Les matières enseignées font partie de la fiche : elles sont archivées avec elle (annoncé à l'écran).
        await EntityDeletionAnalyzer.ArchiveIntrinsicAsync(
            dbContext, TeacherDeletionPolicy.Policy, request.Id, actorId.ToString(), cancellationToken);

        // Même verrou optimiste que UpdateTeacherCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(teacher, request.RowVersion);

        teacher.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
