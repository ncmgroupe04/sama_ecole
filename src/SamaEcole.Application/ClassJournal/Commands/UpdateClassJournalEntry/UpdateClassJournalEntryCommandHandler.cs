using SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ClassJournal.Commands.UpdateClassJournalEntry;

public class UpdateClassJournalEntryCommandHandler(
    IApplicationDbContext dbContext,
    ClassJournalScopeAuthorizer scopeAuthorizer,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateClassJournalEntryCommand, ClassJournalEntryResult>
{
    public async Task<ClassJournalEntryResult> Handle(
        UpdateClassJournalEntryCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une entrée d'une autre école renvoie 404, jamais une modification silencieuse.
        var entry = await dbContext.ClassJournalEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Entrée de journal {request.Id} introuvable.");

        if (request.HomeworkDueDate is { } dueDate && dueDate < entry.SessionDate)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.HomeworkDueDate), "La date de rendu ne peut pas précéder la date de la séance.")
            ]);
        }

        var ownTeacherId = await scopeAuthorizer.GetOwnTeacherIdOrNullAsync(cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        ClassJournalEditWindow.EnsureCanCorrect(entry, ownTeacherId, today);

        // Verrou optimiste (AGENTS.md règle #5) : une entrée modifiée en base depuis sa lecture fait
        // échouer SaveChangesAsync en 409, jamais un écrasement silencieux.
        dbContext.SetOriginalConcurrencyToken(entry, request.RowVersion);

        entry.Topic = request.Topic;
        entry.Content = request.Content;
        entry.Homework = request.Homework;
        entry.HomeworkDueDate = request.HomeworkDueDate;

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.ClassJournalEntries.AsNoTracking()
            .Where(e => e.Id == entry.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .FirstAsync(cancellationToken);

        return new ClassJournalEntryResult(
            entry.Id, entry.ClassroomId, entry.SubjectId, entry.TeacherId, entry.SessionDate,
            entry.Topic, entry.Content, entry.Homework, entry.HomeworkDueDate, newRowVersion);
    }
}
