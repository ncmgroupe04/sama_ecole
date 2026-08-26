using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.DeleteInventoryCategory;

public class DeleteInventoryCategoryCommandValidator : AbstractValidator<DeleteInventoryCategoryCommand>
{
    public DeleteInventoryCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
