using FluentValidation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Commands.SelectProfile;

public class SelectProfileCommandValidator : AbstractValidator<SelectProfileCommand>
{
    public SelectProfileCommandValidator()
    {
        RuleFor(c => c.Profile)
            .IsInEnum().WithMessage("Profil d'établissement invalide.");

        RuleFor(c => c.Tier)
            .IsInEnum().WithMessage("Tranche d'effectif invalide.")
            .NotEqual(StudentQuotaTier.Tier4_Custom)
            .WithMessage("La tranche sur mesure est fixée par l'équipe commerciale : choisissez une des trois tranches proposées.");
    }
}
