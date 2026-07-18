using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;

public class CreateSchoolYearCommandValidator : AbstractValidator<CreateSchoolYearCommand>
{
    /// <summary>
    /// Bornes de durée. Une année scolaire sénégalaise court d'octobre à juillet (~10 mois) ; ces
    /// bornes ne cherchent pas à l'imposer — un établissement reste libre de ses dates — mais à
    /// intercepter la faute de frappe qui, elle, est irrattrapable : « 2026 » saisi au lieu de
    /// « 2027 » créerait une année de 74 ans, qui chevaucherait toutes les suivantes et bloquerait
    /// leur création sans que personne ne comprenne pourquoi.
    /// </summary>
    private const int MinMonths = 3;
    private const int MaxMonths = 18;

    public CreateSchoolYearCommandValidator()
    {
        // Libellé LIBRE : « 2026-2027 » au Sénégal, mais aucun format n'est imposé — c'est un texte
        // d'affichage, jamais une clé de calcul (voir SchoolYear).
        RuleFor(x => x.Label).NotEmpty().MaximumLength(20).NoHtml();

        RuleFor(x => x.StartDate)
            .NotEqual(default(DateOnly)).WithMessage("La date de début est obligatoire.");

        RuleFor(x => x.EndDate)
            .Cascade(CascadeMode.Stop)
            .NotEqual(default(DateOnly)).WithMessage("La date de fin est obligatoire.")
            .GreaterThan(x => x.StartDate).WithMessage("La date de fin doit être postérieure à la date de début.")
            .Must((command, endDate) => endDate >= command.StartDate.AddMonths(MinMonths))
                .WithMessage($"Une année scolaire dure au moins {MinMonths} mois.")
            .Must((command, endDate) => endDate <= command.StartDate.AddMonths(MaxMonths))
                .WithMessage($"Une année scolaire ne peut pas s'étendre sur plus de {MaxMonths} mois — vérifiez les dates saisies.");
    }
}
