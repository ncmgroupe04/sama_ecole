using FluentValidation;

namespace SamaEcole.Application.SchoolYears.Commands.DeleteSchoolYear;

/// <summary>
/// Ne vérifie ici que la PRÉSENCE de la confirmation. Sa valeur attendue — le libellé exact de
/// l'année — est comparée dans le Handler : ce validateur, sans accès à la base, ne peut pas la
/// connaître (même partage des rôles que ResetSchoolDataCommandValidator).
/// </summary>
public class DeleteSchoolYearCommandValidator : AbstractValidator<DeleteSchoolYearCommand>
{
    public DeleteSchoolYearCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.Confirmation)
            .NotEmpty()
            .WithMessage("Saisissez le libellé exact de l'année scolaire pour confirmer.")
            .MaximumLength(20);
    }
}
