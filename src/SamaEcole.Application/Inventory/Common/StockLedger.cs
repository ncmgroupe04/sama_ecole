using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;

namespace SamaEcole.Application.Inventory.Common;

/// <summary>
/// Point de passage UNIQUE de toute variation de stock. Aucun Handler ne modifie
/// <c>InventoryItem.QuantityTotal</c> ni <c>QuantityAvailable</c> directement : il appelle
/// <see cref="Apply"/>, qui met à jour les compteurs ET produit la ligne de journal correspondante,
/// snapshots compris.
///
/// Pourquoi un point unique : quatre Handlers font varier le stock (mouvement manuel, attribution,
/// restitution, annulation d'une fiche). Laisser chacun écrire ses deux compteurs et sa ligne de
/// journal, c'est garantir qu'un jour l'un des quatre oubliera le journal, ou décrémentera le mauvais
/// compteur — et un inventaire dont le journal ne recolle plus avec les compteurs n'est plus
/// opposable lors d'un contrôle.
///
/// Cette classe ne persiste rien et n'ouvre aucune transaction : elle mute l'entité SUIVIE que le
/// Handler lui passe et renvoie le mouvement à ajouter. C'est le SaveChangesAsync du Handler — sous
/// verrou optimiste xmin posé sur le lot (AGENTS.md règle #5) — qui décide de tout ou rien.
/// </summary>
public static class StockLedger
{
    /// <summary>
    /// Applique <paramref name="quantity"/> unités de <paramref name="type"/> à <paramref name="item"/>
    /// et retourne la ligne de journal à insérer.
    /// </summary>
    /// <exception cref="ValidationException">
    /// 422 — l'opération sortirait le lot de ses bornes (disponible négatif, ou disponible supérieur
    /// au total). Une VALIDATION, pas un conflit : la requête est refusée pour ce qu'elle demande, pas
    /// parce qu'une écriture concurrente l'a doublée (ce cas-là remonte en 409 depuis SaveChangesAsync).
    /// </exception>
    public static StockMovement Apply(
        InventoryItem item,
        StockMovementType type,
        int quantity,
        DateOnly movementDate,
        string reason,
        string? counterpartyLabel = null,
        Guid? assignmentId = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (quantity <= 0)
        {
            throw Invalid(nameof(quantity), "La quantité d'un mouvement de stock doit être strictement positive.");
        }

        var (totalDelta, availableDelta) = DeltasFor(type, quantity);

        var newTotal = item.QuantityTotal + totalDelta;
        var newAvailable = item.QuantityAvailable + availableDelta;

        if (newAvailable < 0)
        {
            throw Invalid(
                nameof(quantity),
                $"Quantité insuffisante : {item.QuantityAvailable} unité(s) disponible(s) sur « {item.Name} », " +
                $"{quantity} demandée(s).");
        }

        if (newTotal < 0)
        {
            throw Invalid(
                nameof(quantity),
                $"Quantité insuffisante : le lot « {item.Name} » ne compte que {item.QuantityTotal} unité(s) au total.");
        }

        // Un disponible supérieur au total signalerait une restitution de plus d'unités qu'il n'en a
        // jamais été prêté — la contrainte CHECK le refuserait en 500 ; autant l'expliquer en 422.
        if (newAvailable > newTotal)
        {
            throw Invalid(
                nameof(quantity),
                $"Restitution impossible : elle porterait le disponible de « {item.Name} » ({newAvailable}) " +
                $"au-delà de son effectif total ({newTotal}).");
        }

        item.QuantityTotal = newTotal;
        item.QuantityAvailable = newAvailable;

        return new StockMovement
        {
            SchoolId = item.SchoolId,
            ItemId = item.Id,
            Type = type,
            Quantity = quantity,
            MovementDate = movementDate,
            Reason = reason,
            CounterpartyLabel = counterpartyLabel,
            AssignmentId = assignmentId,
            QuantityTotalAfter = newTotal,
            QuantityAvailableAfter = newAvailable
        };
    }

    /// <summary>
    /// L'arithmétique de chaque type, en un seul endroit. Voir <see cref="StockMovementType"/> pour
    /// la justification métier de chaque ligne — notamment celles qui partagent le même calcul.
    /// </summary>
    private static (int TotalDelta, int AvailableDelta) DeltasFor(StockMovementType type, int quantity) => type switch
    {
        StockMovementType.Entree => (+quantity, +quantity),
        StockMovementType.AjustementPositif => (+quantity, +quantity),

        StockMovementType.Sortie => (-quantity, -quantity),
        StockMovementType.AjustementNegatif => (-quantity, -quantity),
        StockMovementType.MiseAuRebut => (-quantity, -quantity),

        // Le bien reste au patrimoine tant qu'il est prêté : seul le disponible bouge.
        StockMovementType.Attribution => (0, -quantity),
        StockMovementType.Restitution => (0, +quantity),

        // Déjà hors du disponible depuis l'attribution : seul le total bouge.
        StockMovementType.PerteSurPret => (-quantity, 0),

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Type de mouvement de stock inconnu.")
    };

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
