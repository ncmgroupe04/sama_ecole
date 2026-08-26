using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.RecordStockMovement;

public class RecordStockMovementCommandHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<RecordStockMovementCommand, StockMovementResult>
{
    public async Task<StockMovementResult> Handle(
        RecordStockMovementCommand request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var movementDate = request.MovementDate ?? today;

        if (movementDate > today)
        {
            throw Invalid(nameof(request.MovementDate), "La date d'un mouvement de stock ne peut pas être dans le futur.");
        }

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // le bien d'une autre école renvoie 404, jamais un mouvement silencieux sur son stock.
        var item = await dbContext.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {request.ItemId} introuvable.");

        // Le verrou est posé AVANT tout calcul : les compteurs lus ci-dessous sont ceux que le client
        // avait sous les yeux, et SaveChangesAsync refusera en 409 si la ligne a bougé entre-temps.
        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        var (type, quantity) = Translate(request, item.QuantityTotal);

        var movement = StockLedger.Apply(
            item,
            type,
            quantity,
            movementDate,
            request.Reason.Trim(),
            string.IsNullOrWhiteSpace(request.CounterpartyLabel) ? null : request.CounterpartyLabel.Trim());

        dbContext.StockMovements.Add(movement);

        await dbContext.SaveChangesAsync(cancellationToken);

        var itemRowVersion = await dbContext.InventoryItems.AsNoTracking()
            .Where(i => i.Id == item.Id)
            .Select(i => EF.Property<uint>(i, "xmin"))
            .FirstAsync(cancellationToken);

        return new StockMovementResult(
            movement.Id,
            movement.ItemId,
            movement.Type.ToString(),
            movement.Quantity,
            movement.MovementDate,
            movement.QuantityTotalAfter,
            movement.QuantityAvailableAfter,
            itemRowVersion);
    }

    /// <summary>
    /// Traduit le verbe de l'API en ligne de journal. Seul l'ajustement demande un vrai travail : la
    /// requête porte l'effectif COMPTÉ, le journal doit porter l'ÉCART et son sens — c'est ce qui rend
    /// une ligne isolée lisible sans rejouer l'historique (voir StockMovementType).
    /// </summary>
    private static (StockMovementType Type, int Quantity) Translate(
        RecordStockMovementCommand request, int currentTotal) => request.Type switch
    {
        StockMovementRequestType.Entree => (StockMovementType.Entree, request.Quantity),
        StockMovementRequestType.Sortie => (StockMovementType.Sortie, request.Quantity),
        StockMovementRequestType.MiseAuRebut => (StockMovementType.MiseAuRebut, request.Quantity),

        StockMovementRequestType.Ajustement => TranslateAdjustment(request.Quantity, currentTotal),

        _ => throw Invalid(nameof(request.Type), "Type de mouvement de stock inconnu.")
    };

    private static (StockMovementType Type, int Quantity) TranslateAdjustment(int countedTotal, int currentTotal)
    {
        var ecart = countedTotal - currentTotal;

        if (ecart > 0)
        {
            return (StockMovementType.AjustementPositif, ecart);
        }

        if (ecart < 0)
        {
            return (StockMovementType.AjustementNegatif, -ecart);
        }

        // Un comptage identique à la fiche n'est pas une erreur de l'utilisateur, mais ce n'est pas un
        // mouvement non plus : écrire une ligne de zéro unité polluerait le journal et violerait la
        // contrainte CHECK Quantity > 0.
        throw Invalid(
            nameof(RecordStockMovementCommand.Quantity),
            $"Aucun ajustement à enregistrer : l'effectif compté ({countedTotal}) est déjà celui de la fiche.");
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
