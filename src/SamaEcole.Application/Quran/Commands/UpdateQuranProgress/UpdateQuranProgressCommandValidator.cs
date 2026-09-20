using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranProgress;

public class UpdateQuranProgressCommandValidator : AbstractValidator<UpdateQuranProgressCommand>
{
    public UpdateQuranProgressCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Status).IsInEnum().WithMessage("Statut de mémorisation invalide.");
        RuleFor(c => c.Notes).MaximumLength(2000).NoHtml();
    }
}
