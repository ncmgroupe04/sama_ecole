using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
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

        // Règle métier obligatoire : un enseignant déjà attribué à une classe/matière/année ne peut
        // pas être archivé — l'historique des affectations (JGK-D04) référence sa fiche.
        var hasAssignments = await dbContext.TeacherAssignments
            .AnyAsync(a => a.TeacherId == request.Id, cancellationToken);

        if (hasAssignments)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : cet élément possède des données liées (des attributions classe/matière existent déjà pour cet enseignant).");
        }

        // Même verrou optimiste que UpdateTeacherCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(teacher, request.RowVersion);

        teacher.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
