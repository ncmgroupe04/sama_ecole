using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.ClassJournal.Commands.UpdateClassJournalEntry;

public class UpdateClassJournalEntryCommandValidator : AbstractValidator<UpdateClassJournalEntryCommand>
{
    public UpdateClassJournalEntryCommandValidator()
    {
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(4000).NoHtml();
        RuleFor(x => x.Homework).MaximumLength(2000).NoHtml();

        RuleFor(x => x.HomeworkDueDate)
            .Null()
            .When(x => string.IsNullOrWhiteSpace(x.Homework))
            .WithMessage("Une date de rendu suppose un devoir renseigné.");
    }
}
