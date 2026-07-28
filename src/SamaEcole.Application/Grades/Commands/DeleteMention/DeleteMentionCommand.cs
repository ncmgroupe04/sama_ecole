using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.DeleteMention;

/// <summary>
/// DELETE /grades/mentions/{id} — ticket JGK-G02. Suppression LOGIQUE uniquement (AGENTS.md règle #6) :
/// une mention effacée par erreur de saisie ne doit pas laisser de trou silencieux dans l'historique.
/// L'index unique (SchoolId, Label, IsDeleted) inclut IsDeleted : un libellé supprimé redevient donc
/// immédiatement réutilisable pour une nouvelle mention, sans collision.
/// </summary>
public record DeleteMentionCommand(Guid Id) : IRequest;

public class DeleteMentionCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteMentionCommand>
{
    public async Task Handle(DeleteMentionCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var mention = await dbContext.Mentions
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mention {request.Id} introuvable.");

        mention.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
