using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class PaymentBreakdown : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid PaymentId { get; set; }

    public Guid FeeCategoryId { get; set; }

    public decimal AmountAllocated { get; set; }

    /// <summary>
    /// Libellé libre de la colonne « Période / Note » du reçu de caisse (« Unique », « 2 jeux »,
    /// « Rattrapage octobre »), saisi au guichet. FACULTATIF : laissé vide, le reçu retombe sur
    /// <see cref="Payment.ReferencePeriod"/>, la période du versement entier — c'est le cas courant,
    /// une caisse ne saisit un libellé par ligne que lorsqu'il diffère de cette période.
    ///
    /// Purement descriptif : aucun calcul ne s'en sert, et il n'entre dans aucun total. Figé comme le
    /// reste de la ligne — réimprimer le reçu six mois plus tard doit redonner le libellé de CE
    /// versement, pas un libellé réécrit depuis.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// MOIS CIVIL (1-12) que cette ligne règle, choisi par le caissier quand l'école a activé « demander le mois à
    /// l'encaissement » (SchoolSettings.AskPaymentMonthAtCashier). Contrairement à <see cref="Label"/>, il ENTRE dans le
    /// calcul : l'argent de cette ligne va sur ce mois (TaggedInstallmentAllocator). Null pour tout paiement ancien
    /// ou toute ligne sans choix de mois : l'imputation « dans l'ordre » s'applique alors, inchangée.
    /// </summary>
    public int? Month { get; set; }

    public Payment Payment { get; set; } = null!;

    public FeeCategory FeeCategory { get; set; } = null!;
}
