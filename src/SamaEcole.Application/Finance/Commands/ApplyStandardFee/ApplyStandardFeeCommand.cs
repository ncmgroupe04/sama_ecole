using MediatR;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

/// <summary>
/// POST /api/v1/finance/fees/apply-standard — ticket JGK-F01, « Option 1 : définition globale ».
///
/// Applique un montant standard aux classes de l'établissement pour une catégorie, en un seul geste
/// (Volume 1 §7.4). C'est ce qui évite de saisir le même montant classe par classe.
///
/// <see cref="Level"/> délimite le périmètre : une école facture rarement le Primaire au même tarif
/// que le Lycée, et une remise à plat d'un cycle ne doit pas déborder sur les autres.
///
/// <see cref="OverwriteExisting"/> arbitre le sort des lignes DÉJÀ définies, à l'intérieur du
/// périmètre uniquement :
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

    /// <summary>
    /// Périmètre d'application. NULL ou vide = toutes les classes de l'établissement (comportement
    /// d'origine). Sinon, seules les classes de CE niveau sont touchées ; les autres cycles gardent
    /// leur montant, y compris avec <see cref="OverwriteExisting"/>.
    ///
    /// Comparé sans tenir compte de la casse ni des espaces de bord : le niveau est une nomenclature
    /// LIBRE saisie par l'école (voir CreateClassroomCommandValidator), pas une énumération — « Collège »
    /// et « collège » désignent le même cycle et doivent cibler les mêmes classes.
    /// </summary>
    public string? Level { get; init; }
}

/// <summary>Compte-rendu de l'opération : combien de classes créées, réalignées, laissées telles quelles.</summary>
public record ApplyStandardFeeResult(int Created, int Updated, int Skipped);
