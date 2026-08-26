using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.CancelItemAssignment;

public class CancelItemAssignmentCommandValidator : AbstractValidator<CancelItemAssignmentCommand>
{
    public CancelItemAssignmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
