using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subjects.Commands.DeleteSubject;

public class DeleteSubjectCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteSubjectCommand, Unit>
{
    public async Task<Unit> Handle(DeleteSubjectCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une matière d'une autre école renvoie 404, jamais une suppression silencieuse.
        var subject = await dbContext.Subjects
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Matière {request.Id} introuvable.");

        // Règle métier obligatoire : une matière déjà notée ne peut pas être archivée — son coefficient
        // pilote encore le calcul des moyennes et bulletins déjà émis (Subject.Coefficient).
        var hasGrades = await dbContext.Grades
            .AnyAsync(g => g.SubjectId == request.Id, cancellationToken);

        if (hasGrades)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : cet élément possède des données liées (des notes existent déjà pour cette matière).");
        }

        // Même verrou optimiste que UpdateSubjectCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(subject, request.RowVersion);

        subject.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
