using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.UpdateInventoryCategory;

public class UpdateInventoryCategoryCommandValidator : AbstractValidator<UpdateInventoryCategoryCommand>
{
    public UpdateInventoryCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Description).MaximumLength(300).NoHtml();
    }
}
