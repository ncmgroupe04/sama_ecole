using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.DeleteInventoryItem;

public class DeleteInventoryItemCommandValidator : AbstractValidator<DeleteInventoryItemCommand>
{
    public DeleteInventoryItemCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
