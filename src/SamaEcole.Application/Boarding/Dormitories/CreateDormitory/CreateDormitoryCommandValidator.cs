using FluentValidation;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Dormitories.CreateDormitory;

public class CreateDormitoryCommandValidator : AbstractValidator<CreateDormitoryCommand>
{
    public CreateDormitoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Gender).IsInEnum().WithMessage("Genre de pavillon invalide.")
            .NotEqual(DormitoryGender.Mixte)
            .WithMessage("Choisissez Garçons ou Filles : « Mixte » n'existe que pour les pavillons repris de l'ancien module.");
        RuleFor(x => x.SupervisorName).MaximumLength(150).NoHtml();
        RuleFor(x => x.SupervisorPhone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        RuleFor(x => x.Notes).MaximumLength(1000).NoHtml();
    }
}
