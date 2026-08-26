using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.CreateInventoryCategory;

public class CreateInventoryCategoryCommandValidator : AbstractValidator<CreateInventoryCategoryCommand>
{
    public CreateInventoryCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Description).MaximumLength(300).NoHtml();
    }
}
