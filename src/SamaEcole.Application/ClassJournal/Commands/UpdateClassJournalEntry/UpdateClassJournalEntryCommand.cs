using SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;
using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.ClassJournal.Commands.UpdateClassJournalEntry;

/// <summary>
/// Ticket JGK-P04. Ne porte ni ClassroomId ni SubjectId ni SessionDate : ce sont les coordonnées de
/// la séance journalisée, pas son compte-rendu — les changer reviendrait à créer une autre entrée
/// (supprimer puis recréer), jamais à « corriger » celle-ci.
///
/// <see cref="IAuditableRequest"/> posé d'office (même esprit qu'UpdateGradeCommand) : une
/// correction pédagogique est une écriture sensible, historisée qu'elle vienne de l'auteur (dans
/// les 15 jours) ou d'un rôle élevé (après).
/// </summary>
public record UpdateClassJournalEntryCommand : IRequest<ClassJournalEntryResult>, IAuditableRequest
{
    public required Guid Id { get; init; }
    public required string Topic { get; init; }
    public required string Content { get; init; }
    public string? Homework { get; init; }
    public DateOnly? HomeworkDueDate { get; init; }
    public required uint RowVersion { get; init; }
}
