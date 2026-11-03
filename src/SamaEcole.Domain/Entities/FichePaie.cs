using SamaEcole.Domain.Enums;
using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

public class FichePaie : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EmployeeContractId { get; set; }
    public EmployeeContract EmployeeContract { get; set; } = null!;

    public int Month { get; set; }
    public int Year { get; set; }

    public decimal HoursWorked { get; set; }
    
    public decimal GrossSalary { get; set; }
    public decimal TransportAllowance { get; set; }
    
    public decimal IpresEmployee { get; set; }
    public decimal IpresEmployer { get; set; }
    
    public decimal CssEmployer { get; set; }
    
    public decimal Vrs { get; set; }
    public decimal Brs { get; set; }
    
    public decimal NetSalary { get; set; }

    /// <summary>
    /// Instantané du régime de la paie à la génération : faux = fiche « sans retenues » (net = brut + transport, aucune
    /// cotisation ni retenue). Conservé sur la fiche et non relu sur le contrat : changer le régime d'un contrat ne
    /// réécrit jamais une fiche déjà éditée, et le bulletin PDF reste fidèle à ce qui a été payé.
    /// </summary>
    public bool DeductionsApplied { get; set; } = true;

    // ----- Annulation (Volume 1 §14.5) -----
    // Une fiche ne se supprime ni ne se réécrit : elle s'ANNULE (la ligne reste, avec auteur, date et motif)
    // puis, au besoin, une nouvelle fiche est régénérée pour la même période. Seules les fiches non annulées
    // comptent pour l'unicité (contrat, mois, année), pour la déclaration fiscale et pour la trésorerie.

    /// <summary>Non null = fiche annulée. Irréversible.</summary>
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }

    /// <summary>Fiche qui remplace celle-ci après une régénération, ou null.</summary>
    public Guid? ReplacedByFichePaieId { get; set; }

    // ----- Règlement (Volume 1 §14.6) -----
    // Le règlement crée un décaissement « Salaires » (trésorerie/comptabilité) ; l'annulation d'une fiche payée
    // le contre-passe. Aucune clé étrangère : la ligne de décaissement est un registre immuable que la purge
    // d'école et l'historique doivent pouvoir traiter indépendamment.

    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaidByUserId { get; set; }
    public PaymentMethod? PaidWith { get; set; }
    public Guid? DisbursementId { get; set; }

    public bool IsCancelled => CancelledAt is not null;
    public bool IsPaid => PaidAt is not null;
}
