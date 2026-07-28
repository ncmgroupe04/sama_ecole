using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Encaissement d'un versement contre une inscription (ticket JGK-F02, docs/Volume_3_DDS.md §5.5). C'est
/// l'acte de CAISSE : le service Finance encaisse ce qu'un parent verse sur le <c>TotalDue</c> figé à
/// l'inscription. La Finance ne fixe ni ne modifie jamais ce montant dû (AGENTS.md règle #4) — elle ne
/// fait qu'y imputer des versements.
///
/// Le SOLDE ne vit pas ici mais sur l'inscription : <see cref="Enrollment.AmountPaid"/> est le cumul des
/// versements, et le verrou optimiste (xmin) de l'inscription sérialise deux encaissements concurrents
/// sur le même solde (AGENTS.md règle #5, critère obligatoire du ticket) — jamais un sur-crédit silencieux.
///
/// Aucune suppression physique (règle #6) : une correction future passe par un statut
/// <see cref="PaymentStatus.Cancelled"/>, pas par un DELETE.
/// </summary>
public class Payment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid? CashierSessionId { get; set; }

    public PaymentCategory Category { get; set; } = PaymentCategory.Tuition;

    public string? ReferencePeriod { get; set; }

    /// <summary>Montant versé, en FCFA. Strictement positif, et jamais supérieur au solde restant.</summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Taux de TVA appliqué à ce versement (fraction, ex. 0.18 pour 18 %), ou null si non assujetti —
    /// les frais de scolarité (PaymentCategory.Tuition) sont typiquement exonérés au Sénégal, jamais
    /// une hypothèse codée en dur ici : c'est la Finance qui déclare l'assujettissement au moment de
    /// l'encaissement, transaction par transaction (aucune règle générale par catégorie n'est fiable).
    /// </summary>
    public decimal? VatRate { get; set; }

    /// <summary>
    /// Part de TVA comprise dans <see cref="Amount"/> (TTC), figée au moment de l'encaissement — jamais
    /// recalculée depuis un taux qui pourrait changer plus tard (même logique de fidélité que
    /// <see cref="BalanceAfter"/>). Zéro quand <see cref="VatRate"/> est null.
    /// </summary>
    public decimal VatAmount { get; set; }

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    /// <summary>
    /// Solde restant dû (en FCFA) JUSTE APRÈS ce versement, figé à l'encaissement. Un reçu est une pièce
    /// à un instant T : le réimprimer des mois plus tard doit montrer le solde de CE jour-là, pas l'état
    /// courant de l'inscription (même logique de fidélité que les lignes figées du reçu d'inscription,
    /// JGK-E02). « Déjà réglé » sur le reçu se déduit alors de <c>TotalDue − BalanceAfter</c>.
    /// </summary>
    public decimal BalanceAfter { get; set; }

    /// <summary>
    /// Numéro officiel du reçu de paiement (ex. « REC-2025-0007 »). Attribué UNE FOIS dans la transaction
    /// d'encaissement (AGENTS.md règle #3), depuis le même registre de reçus gapless par établissement que
    /// le reçu d'inscription (JGK-E02) : un seul carnet de reçus officiel par école, jamais de trou.
    /// </summary>
    public string ReceiptNumber { get; set; } = string.Empty;

    /// <summary>Utilisateur (module Finance/Directeur) qui a encaissé — issu du JWT, jamais du client.</summary>
    public Guid ReceivedByUserId { get; set; }

    public DateTimeOffset PaidAt { get; set; }

    public Enrollment Enrollment { get; set; } = null!;

    public CashierSession? CashierSession { get; set; }

    public ICollection<PaymentBreakdown> Breakdowns { get; set; } = new List<PaymentBreakdown>();
}
