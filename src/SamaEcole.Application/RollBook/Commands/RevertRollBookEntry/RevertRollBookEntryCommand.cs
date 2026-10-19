using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.RollBook.Commands.RevertRollBookEntry;

/// <summary>
/// DELETE /api/v1/roll-book/entries/{id} — retire la saisie d'une case : elle retrouve le statut calculé à
/// partir des appels par séance. Suppression LOGIQUE (règle #6).
/// </summary>
public record RevertRollBookEntryCommand(Guid EntryId) : IRequest;

public class RevertRollBookEntryCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    RollBookScopeAuthorizer scope,
    ILogger<RevertRollBookEntryCommandHandler> logger)
    : IRequestHandler<RevertRollBookEntryCommand>
{
    public async Task Handle(RevertRollBookEntryCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur non identifié.");

        // Global Query Filter + RLS : la saisie d'une autre école est introuvable (404).
        var entry = await dbContext.RollBookEntries.FirstOrDefaultAsync(e => e.Id == request.EntryId, cancellationToken)
            ?? throw new KeyNotFoundException($"Saisie {request.EntryId} introuvable.");

        await scope.EnsureCanAccessClassroomAsync(entry.ClassroomId, entry.SchoolYearId, cancellationToken);

        entry.SoftDelete(actorId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Saisie {EntryId} du cahier de roulement remise au statut calculé par {UserId}.", entry.Id, actorId);
    }
}
