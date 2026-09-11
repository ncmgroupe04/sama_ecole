using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.SetMatriculeSequenceStart;

public class SetMatriculeSequenceStartCommandValidator : AbstractValidator<SetMatriculeSequenceStartCommand>
{
    /// <summary>
    /// Même borne haute que <c>MatriculeFormat.Validate</c>, qui teste le rendu sur 999 999 : au-delà,
    /// le matricule produit risquerait de dépasser la longueur de colonne. C'est aussi un garde-fou
    /// contre la faute de frappe — un zéro de trop.
    /// </summary>
    private const int MaxNextValue = 999_999;

    public SetMatriculeSequenceStartCommandValidator()
    {
        RuleFor(x => x.Kind)
            .Must(k => k is MatriculeKind.Student or MatriculeKind.Teacher)
            .WithMessage("Seuls les compteurs d'élèves et d'enseignants sont réglables.");

        RuleFor(x => x.NextValue)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Le prochain numéro doit être au moins 1.")
            .LessThanOrEqualTo(MaxNextValue)
            .WithMessage($"Le prochain numéro ne peut pas dépasser {MaxNextValue}.");
    }
}
