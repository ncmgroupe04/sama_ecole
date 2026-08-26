using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.CreateInventoryItem;

public class CreateInventoryItemCommandValidator : AbstractValidator<CreateInventoryItemCommand>
{
    public CreateInventoryItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).NoHtml();
        RuleFor(x => x.Code).MaximumLength(50).NoHtml();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.LocationLabel).MaximumLength(100).NoHtml();
        RuleFor(x => x.Notes).MaximumLength(500).NoHtml();

        // Zéro est légitime : on crée la fiche d'un bien attendu mais pas encore livré.
        RuleFor(x => x.InitialQuantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("La quantité initiale ne peut pas être négative.");

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.UnitPrice.HasValue)
            .WithMessage("Le prix unitaire indicatif ne peut pas être négatif.");
    }
}
