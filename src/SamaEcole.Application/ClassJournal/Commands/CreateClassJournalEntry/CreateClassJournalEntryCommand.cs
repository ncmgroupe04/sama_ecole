using MediatR;

namespace SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;

/// <summary>
/// Ticket JGK-P04. Ni TeacherId ni SchoolId dans la charge utile : l'auteur vient du JWT via
/// ClassJournalScopeAuthorizer, l'école de ITenantProvider (AGENTS.md règle #10) — jamais du client.
/// </summary>
public record CreateClassJournalEntryCommand : IRequest<ClassJournalEntryResult>
{
    public required Guid ClassroomId { get; init; }
    public required Guid SubjectId { get; init; }
    public required DateOnly SessionDate { get; init; }
    public required string Topic { get; init; }
    public required string Content { get; init; }
    public string? Homework { get; init; }
    public DateOnly? HomeworkDueDate { get; init; }

    /// <summary>
    /// Unités du programme traitées pendant la séance (Évolution N°7 — suivi du syllabus) : elles doivent appartenir au
    /// programme de la matière pour le niveau de la classe. Absent : aucune unité pointée.
    /// </summary>
    public IReadOnlyList<Guid>? SyllabusUnitIds { get; init; }
}

public record ClassJournalEntryResult(
    Guid Id,
    Guid ClassroomId,
    Guid SubjectId,
    Guid TeacherId,
    DateOnly SessionDate,
    string Topic,
    string Content,
    string? Homework,
    DateOnly? HomeworkDueDate,
    uint RowVersion);
