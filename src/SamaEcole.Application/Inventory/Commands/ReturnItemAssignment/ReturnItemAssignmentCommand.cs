using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.ReturnItemAssignment;

/// <summary>
/// POST /api/v1/inventory/assignments/{id}/return — enregistre la restitution, totale ou partielle,
/// d'un prêt. Appelable plusieurs fois sur la même fiche : un élève peut rapporter huit manuels en
/// juin et les quatre derniers en septembre. <see cref="ReturnedQuantity"/> est ce qui est rendu CE
/// JOUR-LÀ, pas le cumul — le Handler cumule.
///
/// <see cref="RowVersion"/> est le jeton de la FICHE : deux surveillants qui clôturent le même prêt
/// en même temps ne doivent pas créditer le stock deux fois (AGENTS.md règle #5).
/// </summary>
public record ReturnItemAssignmentCommand : IRequest<ItemReturnResult>
{
    public Guid Id { get; init; }

    /// <summary>Unités rendues lors de cette restitution. Zéro est licite si <see cref="DeclareRemainderLost"/> est vrai.</summary>
    public int ReturnedQuantity { get; init; }

    /// <summary>
    /// État constaté au retour. <c>HorsService</c> réforme la part rendue au lieu de la remettre en
    /// circulation : le bien revient bien à l'école, mais pas au stock disponible.
    /// </summary>
    public ItemCondition ReturnCondition { get; init; } = ItemCondition.Bon;

    /// <summary>Date du retour. Par défaut : aujourd'hui.</summary>
    public DateOnly? ReturnedOn { get; init; }

    /// <summary>
    /// Déclare PERDU tout ce qui n'est pas rendu à l'issue de cette restitution : la part manquante
    /// sort définitivement du patrimoine (mouvement PerteSurPret) et la fiche est clôturée.
    /// Sans ce drapeau, une restitution partielle laisse la fiche ouverte, en attente du reste.
    /// </summary>
    public bool DeclareRemainderLost { get; init; }

    public uint RowVersion { get; init; }
}

public record ItemReturnResult(
    Guid Id,
    string Status,
    int ReturnedQuantity,
    int OutstandingQuantity,
    DateOnly ReturnedOn,
    int QuantityAvailableAfter,
    uint RowVersion,
    uint ItemRowVersion);
