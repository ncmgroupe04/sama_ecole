using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.CreateItemAssignment;

/// <summary>
/// POST /api/v1/inventory/assignments — prête ou attribue des unités d'un lot à un élève, un
/// enseignant ou un membre du personnel, et produit la fiche de décharge à faire signer.
///
/// L'API expose UN couple (BeneficiaryType, BeneficiaryId) là où l'entité porte trois clés étrangères
/// nullables : c'est au Handler de router vers la bonne colonne. Le client n'a pas à savoir dans quelle
/// table vit son bénéficiaire, et la contrainte CHECK en base reste le garde-fou.
///
/// <see cref="RowVersion"/> est le jeton du LOT : un prêt fait varier son disponible (règle #5).
/// </summary>
public record CreateItemAssignmentCommand : IRequest<ItemAssignmentResult>
{
    public Guid ItemId { get; init; }

    /// <summary>Douze manuels remis à un même élève tiennent en UNE fiche : la quantité vit ici.</summary>
    public int Quantity { get; init; } = 1;

    public AssignmentBeneficiaryType BeneficiaryType { get; init; }

    /// <summary>Identifiant de l'élève, de l'enseignant ou de l'utilisateur, selon <see cref="BeneficiaryType"/>.</summary>
    public Guid BeneficiaryId { get; init; }

    /// <summary>Date de remise. Par défaut : aujourd'hui. Jamais dans le futur.</summary>
    public DateOnly? AssignedOn { get; init; }

    /// <summary>Retour attendu — typiquement la fin de l'année scolaire pour des manuels. Facultatif.</summary>
    public DateOnly? DueOn { get; init; }

    public string? Notes { get; init; }

    public uint RowVersion { get; init; }
}

public record ItemAssignmentResult(
    Guid Id,
    string Reference,
    Guid ItemId,
    string ItemName,
    int Quantity,
    string BeneficiaryType,
    Guid BeneficiaryId,
    string BeneficiaryLabel,
    DateOnly AssignedOn,
    DateOnly? DueOn,
    string Status,
    uint RowVersion,
    uint ItemRowVersion);
