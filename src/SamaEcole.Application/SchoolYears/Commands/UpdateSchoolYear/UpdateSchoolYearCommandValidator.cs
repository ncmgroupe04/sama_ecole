using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.SchoolYears.Commands.UpdateSchoolYear;

/// <summary>
/// Mêmes règles de saisie que CreateSchoolYearCommandValidator, et volontairement pas moins : une
/// année corrigée doit rester aussi cohérente qu'une année créée. Les bornes de durée interceptent la
/// faute de frappe irrattrapable (« 2026 » au lieu de « 2027 »), qui produirait une année de plusieurs
/// décennies chevauchant toutes les autres.
/// </summary>
public class UpdateSchoolYearCommandValidator : AbstractValidator<UpdateSchoolYearCommand>
{
    private const int MinMonths = 3;
    private const int MaxMonths = 18;

    public UpdateSchoolYearCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

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
