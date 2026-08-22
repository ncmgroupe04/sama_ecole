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

        // Un DOMAINE qui porte encore des activités : les archiver en cascade supprimerait des notes de
        // vue sans que personne ne l'ait demandé, et les laisser en place les rendrait orphelines —
        // rattachées à un domaine archivé, donc absentes du bulletin sans le moindre message. L'école
        // décide, ligne par ligne (la FK est en Restrict pour la même raison).
        var childCount = await dbContext.Subjects
            .CountAsync(s => s.ParentSubjectId == request.Id, cancellationToken);

        if (childCount > 0)
        {
            throw new BusinessRuleException(
                $"Impossible de supprimer : ce domaine porte encore {childCount} activité(s) d'évaluation. Supprimez-les ou rattachez-les à un autre domaine d'abord.");
        }

        // Même verrou optimiste que UpdateSubjectCommandHandler (AGENTS.md règle #5).
        dbContext.SetOriginalConcurrencyToken(subject, request.RowVersion);

        subject.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
