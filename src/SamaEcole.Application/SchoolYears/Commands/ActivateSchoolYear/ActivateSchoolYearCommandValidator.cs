using FluentValidation;

namespace SamaEcole.Application.SchoolYears.Commands.ActivateSchoolYear;

public class ActivateSchoolYearCommandValidator : AbstractValidator<ActivateSchoolYearCommand>
{
    public ActivateSchoolYearCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // Ne dit rien du mot de passe LUI-MÊME : sa longueur ou sa forme ne se valident pas ici, il
        // est comparé au hash du compte. On refuse seulement la confirmation vide, qui ne confirme rien.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Confirmez votre mot de passe pour changer l'année scolaire active.");
    }
}
