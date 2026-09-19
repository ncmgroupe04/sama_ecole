using MediatR;

namespace SamaEcole.Application.ClassJournal.Commands.DeleteClassJournalEntry;

/// <summary>Ticket JGK-P04. Même garde des 15 jours qu'UpdateClassJournalEntryCommand (ClassJournalEditWindow).</summary>
public record DeleteClassJournalEntryCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
