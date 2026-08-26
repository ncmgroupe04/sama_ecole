using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.ReturnItemAssignment;

public class ReturnItemAssignmentCommandValidator : AbstractValidator<ReturnItemAssignmentCommand>
{
    public ReturnItemAssignmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReturnCondition).IsInEnum();

        // Zéro est licite (perte totale déclarée sans aucun retour) ; le Handler refuse le cas
        // « zéro rendu et rien de perdu », qui n'enregistrerait rien du tout.
        RuleFor(x => x.ReturnedQuantity)
            .GreaterThanOrEqualTo(0)
            .WithMessage("La quantité restituée ne peut pas être négative.");
    }
}
