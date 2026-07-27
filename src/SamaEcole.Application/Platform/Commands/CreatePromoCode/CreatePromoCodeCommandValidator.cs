using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Platform.Commands.CreatePromoCode;

public class CreatePromoCodeCommandValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Le code est obligatoire.")
            .MaximumLength(32)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Le code ne peut contenir que des lettres, chiffres, tirets et underscores.")
            .NoHtml();

        RuleFor(x => x.EndDateUtc)
            .GreaterThan(x => x.StartDateUtc)
            .WithMessage("La date de fin doit être postérieure à la date de début.");

        // FreeTrialMonths/FullDiscount n'ont aucun montant à décharger (leur valeur en argent est
        // TOUJOURS zéro) : c'est DurationMonths qui pilote la durée offerte, DiscountValue est ignoré.
        RuleFor(x => x.DurationMonths)
            .NotNull().WithMessage("La durée (en mois) est obligatoire pour ce type de réduction.")
            .GreaterThan(0)
            .When(x => x.DiscountType is PromoDiscountType.FreeTrialMonths or PromoDiscountType.FullDiscount);

        RuleFor(x => x.DurationMonths)
            .GreaterThan(0).WithMessage("La durée doit être un nombre de mois positif.")
            .When(x => x.DurationMonths is not null);

        RuleFor(x => x.DiscountValue)
            .InclusiveBetween(0.01m, 100m)
            .WithMessage("Le pourcentage doit être compris entre 0,01 et 100.")
            .When(x => x.DiscountType == PromoDiscountType.Percentage);

        RuleFor(x => x.DiscountValue)
            .GreaterThan(0).WithMessage("Le montant de la réduction doit être positif.")
            .When(x => x.DiscountType == PromoDiscountType.FixedAmount);

        RuleFor(x => x.MaxUses)
            .GreaterThan(0).WithMessage("La limite d'utilisations doit être positive.")
            .When(x => x.MaxUses is not null);
    }
}
