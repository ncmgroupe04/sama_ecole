using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ClassJournal.Commands.DeleteClassJournalEntry;

public class DeleteClassJournalEntryCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    ClassJournalScopeAuthorizer scopeAuthorizer,
    TimeProvider timeProvider)
    : IRequestHandler<DeleteClassJournalEntryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteClassJournalEntryCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var entry = await dbContext.ClassJournalEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Entrée de journal {request.Id} introuvable.");

        var ownTeacherId = await scopeAuthorizer.GetOwnTeacherIdOrNullAsync(cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        ClassJournalEditWindow.EnsureCanCorrect(entry, ownTeacherId, today);

        dbContext.SetOriginalConcurrencyToken(entry, request.RowVersion);

        entry.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
