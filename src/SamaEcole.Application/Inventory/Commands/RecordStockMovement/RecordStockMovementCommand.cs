using MediatR;

namespace SamaEcole.Application.Inventory.Commands.RecordStockMovement;

/// <summary>
/// Les mouvements qu'un utilisateur SAISIT. Volontairement plus étroit que
/// <see cref="Domain.Enums.StockMovementType"/> : les mouvements d'attribution et de restitution sont
/// produits exclusivement par les endpoints de prêt, pour qu'aucun appel direct à
/// /inventory/movements ne puisse faire varier le disponible sans la fiche de décharge qui l'explique.
/// </summary>
public enum StockMovementRequestType
{
    /// <summary>Réception : dotation, achat, don.</summary>
    Entree,

    /// <summary>Sortie définitive : consommable distribué, transfert vers un autre établissement.</summary>
    Sortie,

    /// <summary>
    /// Résultat d'un inventaire physique. <see cref="RecordStockMovementCommand.Quantity"/> porte
    /// alors l'effectif COMPTÉ, pas un écart : le magasinier saisit ce qu'il a sous les yeux, et le
    /// Handler en déduit le sens et l'ampleur de la correction.
    /// </summary>
    Ajustement,

    /// <summary>Réforme d'un bien présent et hors service (cassé, obsolète).</summary>
    MiseAuRebut
}

/// <summary>
/// POST /api/v1/inventory/movements — module Inventaire.
///
/// <see cref="RowVersion"/> est le jeton du LOT, pas du mouvement : c'est le lot dont les compteurs
/// changent. Le client a lu « 1 disponible » avant de saisir sa sortie ; si quelqu'un d'autre a pris
/// cette unité entre-temps, la requête doit échouer en 409 plutôt que de faire passer le disponible
/// sous zéro (AGENTS.md règle #5).
/// </summary>
public record RecordStockMovementCommand : IRequest<StockMovementResult>
{
    public Guid ItemId { get; init; }

    public StockMovementRequestType Type { get; init; }

    /// <summary>Nombre d'unités du mouvement — ou l'effectif COMPTÉ pour un ajustement.</summary>
    public int Quantity { get; init; }

    /// <summary>Date du mouvement réel. Par défaut : aujourd'hui. Jamais dans le futur.</summary>
    public DateOnly? MovementDate { get; init; }

    public required string Reason { get; init; }

    /// <summary>Fournisseur en entrée, destinataire en sortie. Texte libre.</summary>
    public string? CounterpartyLabel { get; init; }

    public uint RowVersion { get; init; }
}

/// <summary>
/// <see cref="ItemRowVersion"/> est le NOUVEAU jeton du lot : l'écran enchaîne souvent plusieurs
/// mouvements sur le même bien, et sans lui le second partirait en 409 sur un jeton périmé.
/// </summary>
public record StockMovementResult(
    Guid Id,
    Guid ItemId,
    string Type,
    int Quantity,
    DateOnly MovementDate,
    int QuantityTotalAfter,
    int QuantityAvailableAfter,
    uint ItemRowVersion);
