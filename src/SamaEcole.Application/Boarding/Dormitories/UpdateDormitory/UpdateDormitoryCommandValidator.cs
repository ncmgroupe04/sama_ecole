using FluentValidation;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;

public class UpdateDormitoryCommandValidator : AbstractValidator<UpdateDormitoryCommand>
{
    public UpdateDormitoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Gender).IsInEnum().WithMessage("Genre de pavillon invalide.")
            .NotEqual(DormitoryGender.Mixte)
            .WithMessage("Qualifiez ce pavillon : Garçons ou Filles. « Mixte » n'existe que pour les pavillons repris de l'ancien module.");
        RuleFor(x => x.SupervisorName).MaximumLength(150).NoHtml();
        RuleFor(x => x.SupervisorPhone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        RuleFor(x => x.Notes).MaximumLength(1000).NoHtml();
    }
}
