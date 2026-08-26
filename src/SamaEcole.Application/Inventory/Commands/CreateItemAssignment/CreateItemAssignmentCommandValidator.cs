using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.CreateItemAssignment;

public class CreateItemAssignmentCommandValidator : AbstractValidator<CreateItemAssignmentCommand>
{
    public CreateItemAssignmentCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.BeneficiaryId).NotEmpty();
        RuleFor(x => x.BeneficiaryType).IsInEnum();
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("La quantité prêtée doit être strictement positive.");
        RuleFor(x => x.Notes).MaximumLength(500).NoHtml();
    }
}
