using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.UpdateInventoryItem;

public class UpdateInventoryItemCommandValidator : AbstractValidator<UpdateInventoryItemCommand>
{
    public UpdateInventoryItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150).NoHtml();
        RuleFor(x => x.Code).MaximumLength(50).NoHtml();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.LocationLabel).MaximumLength(100).NoHtml();
        RuleFor(x => x.Notes).MaximumLength(500).NoHtml();

        RuleFor(x => x.UnitPrice)
            .GreaterThanOrEqualTo(0)
            .When(x => x.UnitPrice.HasValue)
            .WithMessage("Le prix unitaire indicatif ne peut pas être négatif.");
    }
}
