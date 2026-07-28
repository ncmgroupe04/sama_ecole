using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class Disbursement : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    
    public string Reason { get; set; } = string.Empty;
    public DisbursementCategory Category { get; set; }
    public decimal Amount { get; set; }

    /// <summary>
    /// Taux de TVA payé au fournisseur sur cette dépense (fraction, ex. 0.18 pour 18 %), ou null si non
    /// assujettie (ex. Salaires — une rémunération n'est jamais une livraison taxable). Déclaré par la
    /// Finance transaction par transaction, jamais déduit automatiquement d'une catégorie.
    /// </summary>
    public decimal? VatRate { get; set; }

    /// <summary>
    /// Part de TVA (déductible) comprise dans <see cref="Amount"/>, figée à la saisie — même logique de
    /// fidélité que Payment.VatAmount. Zéro quand <see cref="VatRate"/> est null.
    /// </summary>
    public decimal VatAmount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public DateOnly Date { get; set; }
    public string Beneficiary { get; set; } = string.Empty;
    public string? ReceiptUrl { get; set; }
}
