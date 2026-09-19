using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;

public class CreateClassJournalEntryCommandValidator : AbstractValidator<CreateClassJournalEntryCommand>
{
    public CreateClassJournalEntryCommandValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();

        // On journalise ce qui a été fait, jamais un programme prévisionnel (ticket JGK-P04) —
        // même motif que SubmitAttendanceSheetCommandValidator.
        RuleFor(x => x.SessionDate)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La séance ne peut pas être datée dans le futur.");

        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000).NoHtml();
        RuleFor(x => x.Homework).MaximumLength(2000).NoHtml();

        // HomeworkDueDate n'a de sens que si Homework est renseigné, et ne peut précéder la séance
        // qui les a donnés.
        RuleFor(x => x.HomeworkDueDate)
            .Null()
            .When(x => string.IsNullOrWhiteSpace(x.Homework))
            .WithMessage("Une date de rendu suppose un devoir renseigné.");

        RuleFor(x => x.HomeworkDueDate)
            .GreaterThanOrEqualTo(x => x.SessionDate)
            .When(x => x.HomeworkDueDate is not null)
            .WithMessage("La date de rendu ne peut pas précéder la date de la séance.");
    }
}
