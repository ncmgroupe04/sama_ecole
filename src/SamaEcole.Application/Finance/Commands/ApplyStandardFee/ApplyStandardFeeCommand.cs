using MediatR;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

/// <summary>
/// POST /api/v1/finance/fees/apply-standard — ticket JGK-F01, « Option 1 : définition globale ».
///
/// Applique un montant standard à TOUTES les classes de l'établissement pour une catégorie, en un
/// seul geste (Volume 1 §7.4). C'est ce qui évite de saisir le même montant classe par classe.
///
/// <paramref name="OverwriteExisting"/> arbitre le sort des lignes DÉJÀ définies :
///   - false (défaut) : on ne remplit que les classes encore vierges. Les exceptions déjà ajustées
///     (Option 2) sont PRÉSERVÉES — rejouer un standard ne doit pas effacer un réglage fin.
///   - true : on réaligne aussi les classes existantes sur le montant standard (remise à plat
///     assumée). Chaque écrasement est historisé (ancien → nouveau).
/// </summary>
public record ApplyStandardFeeCommand : IRequest<ApplyStandardFeeResult>
{
    public required Guid FeeCategoryId { get; init; }
    public required decimal Amount { get; init; }
    public bool OverwriteExisting { get; init; }
}

/// <summary>Compte-rendu de l'opération : combien de classes créées, réalignées, laissées telles quelles.</summary>
public record ApplyStandardFeeResult(int Created, int Updated, int Skipped);
