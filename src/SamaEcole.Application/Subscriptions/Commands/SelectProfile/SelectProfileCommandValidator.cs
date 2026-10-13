using FluentValidation;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Commands.SelectProfile;

public class SelectProfileCommandValidator : AbstractValidator<SelectProfileCommand>
{
    public SelectProfileCommandValidator()
    {
        RuleFor(c => c.Profile)
            .IsInEnum().WithMessage("Profil d'établissement invalide.");

        // Au-delà de la dernière tranche standard (400 élèves : Tier3_800 et Tier4_Custom), l'offre « Grand Groupe » est sur
        // devis : seul le Super Admin la fixe (UpdateSubscriptionTierCommand). Une école ne peut pas se l'attribuer ici, sans
        // quoi elle contournerait la limite des 400 élèves sans repasser par un devis.
        RuleFor(c => c.Tier)
            .IsInEnum().WithMessage("Tranche d'effectif invalide.")
            .Must(tier => !Enum.IsDefined(tier) || !QuotaTierResolver.RequiresQuote(tier))
            .WithMessage("Au-delà de 400 élèves, l'offre « Grand Groupe » est établie sur devis par l'équipe commerciale : "
                         + "choisissez une des tranches proposées, ou contactez-nous.");
    }
}
