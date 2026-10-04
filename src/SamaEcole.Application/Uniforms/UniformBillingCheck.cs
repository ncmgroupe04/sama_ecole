using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms;

/// <summary>
/// Module Tenues — décide si une dotation est « facturée ». Pure et sans base : elle ne lit que ce que Finance a
/// déjà posé (les catégories des lignes de frais de l'inscription), et n'écrit jamais dans Finance
/// (AGENTS.md règle #4).
///
/// Une dotation est <see cref="UniformBillingStatus.Billed"/> si et seulement si les trois conditions tiennent :
///   1. le modèle de tenue est rattaché à une catégorie de frais ;
///   2. l'inscription porte une ligne de frais de cette catégorie — la famille a coché le frais optionnel
///      (voir <c>OptionalFeeSelection</c>) ;
///   3. la quantité ne dépasse pas celle que la ligne couvre (<c>UniformItem.QuantityIncluded</c>).
///
/// Sinon <see cref="UniformBillingStatus.NotBilled"/> : la remise reste possible, mais exige un motif
/// (<see cref="UniformDeliveryPolicy"/>). Le résultat est calculé À LA REMISE et figé sur la ligne : un barème
/// modifié ensuite ne réécrit pas l'historique.
/// </summary>
public static class UniformBillingCheck
{
    /// <param name="feeCategoryId">Catégorie de frais du modèle de tenue, ou <c>null</c> s'il n'en a pas.</param>
    /// <param name="quantityIncluded">Nombre d'unités couvertes par la ligne de frais (≥ 1).</param>
    /// <param name="quantity">Nombre d'unités de la dotation.</param>
    /// <param name="billedFeeCategoryIds">Catégories des lignes de frais de l'inscription (<c>EnrollmentFeeLine</c>).</param>
    public static UniformBillingStatus Evaluate(
        Guid? feeCategoryId,
        int quantityIncluded,
        int quantity,
        IReadOnlyCollection<Guid> billedFeeCategoryIds)
    {
        if (feeCategoryId is null) return UniformBillingStatus.NotBilled;
        if (!billedFeeCategoryIds.Contains(feeCategoryId.Value)) return UniformBillingStatus.NotBilled;
        if (quantity > quantityIncluded) return UniformBillingStatus.NotBilled;

        return UniformBillingStatus.Billed;
    }
}
