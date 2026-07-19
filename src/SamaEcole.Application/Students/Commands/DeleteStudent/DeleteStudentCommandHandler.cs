using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Commands.DeleteStudent;

public class DeleteStudentCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteStudentCommand, Unit>
{
    public async Task<Unit> Handle(DeleteStudentCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // un élève d'une autre école renvoie 404, jamais une suppression silencieuse.
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.Id} introuvable.");

        // Règle métier obligatoire : aucun historique comptable (inscription, même annulée — le reçu
        // porte un numéro officiel) ni pédagogique (note) ne doit être attaché à l'élève.
        var hasEnrollment = await dbContext.Enrollments
            .AnyAsync(e => e.StudentId == request.Id, cancellationToken);

        if (hasEnrollment)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : cet élément possède des données liées (une inscription existe déjà pour cet élève).");
        }

        var hasGrades = await dbContext.Grades
            .AnyAsync(g => g.StudentId == request.Id, cancellationToken);

        if (hasGrades)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : cet élément possède des données liées (des notes existent déjà pour cet élève).");
        }

        // Même verrou optimiste que UpdateStudentCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(student, request.RowVersion);

        student.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
