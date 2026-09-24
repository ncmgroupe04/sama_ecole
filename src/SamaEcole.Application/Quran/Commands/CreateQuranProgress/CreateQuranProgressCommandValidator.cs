using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Quran.Commands.CreateQuranProgress;

public class CreateQuranProgressCommandValidator : AbstractValidator<CreateQuranProgressCommand>
{
    public CreateQuranProgressCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.JuzNumber).InclusiveBetween(1, 30);
        RuleFor(c => c.HizbNumber).InclusiveBetween(1, 60);
        RuleFor(c => c.SurahNumber).InclusiveBetween(1, 114);
        RuleFor(c => c.Status).IsInEnum().WithMessage("Statut de mémorisation invalide.");
        RuleFor(c => c.Notes).MaximumLength(2000).NoHtml();
    }
}
