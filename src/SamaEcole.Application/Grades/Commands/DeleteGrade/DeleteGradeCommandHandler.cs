using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.DeleteGrade;

public class DeleteGradeCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteGradeCommand, Unit>
{
    public async Task<Unit> Handle(DeleteGradeCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une note d'une autre école renvoie 404, jamais une annulation silencieuse.
        var grade = await dbContext.Grades
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Note {request.Id} introuvable.");

        // Même verrou optimiste que UpdateGradeCommandHandler (AGENTS.md règle #5) : une note modifiée
        // entre-temps par un autre utilisateur ne doit jamais disparaître en silence.
        dbContext.SetOriginalConcurrencyToken(grade, request.RowVersion);

        grade.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
