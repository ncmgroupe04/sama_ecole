using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Inventory.Commands.RecordStockMovement;

public class RecordStockMovementCommandValidator : AbstractValidator<RecordStockMovementCommand>
{
    public RecordStockMovementCommandValidator()
    {
        RuleFor(x => x.ItemId).NotEmpty();
        RuleFor(x => x.Type).IsInEnum();

        // Le motif n'est pas décoratif : c'est lui qui rend le journal opposable lors d'un contrôle
        // (« dotation mairie 2026 », « casse salle 102 »). D'où NotEmpty et non MaximumLength seul.
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.CounterpartyLabel).MaximumLength(150).NoHtml();

        // Zéro est refusé pour tous les types SAUF l'ajustement, où il signifie « je n'ai rien
        // retrouvé » — un comptage physique parfaitement légitime.
        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .When(x => x.Type != StockMovementRequestType.Ajustement)
            .WithMessage("La quantité doit être strictement positive.");

        RuleFor(x => x.Quantity)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Type == StockMovementRequestType.Ajustement)
            .WithMessage("L'effectif compté ne peut pas être négatif.");
    }
}
