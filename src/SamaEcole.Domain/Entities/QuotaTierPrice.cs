using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Prix publié d'une tranche d'effectif (<see cref="StudentQuotaTier"/>), en FCFA. Une ligne par tranche
/// TARIFÉE (Tier_100, Tier1_150, Tier_250, Tier2_400) ; au-delà, l'offre est « Grand Groupe », sur devis, et
/// aucune ligne n'existe (voir <c>QuotaTierResolver.RequiresQuote</c>).
///
/// Table PLATEFORME, comme <see cref="PromoCode"/> : aucun SchoolId, ni Global Query Filter tenant ni
/// policy RLS — une grille de prix n'appartient à aucune école, elle est lisible par tous (formulaire
/// d'inscription public) et modifiée par le seul Super Admin. Verrou optimiste xmin.
///
/// Les deux périodes sont saisies explicitement, jamais déduites l'une de l'autre : le forfait annuel est
/// une remise commerciale (ex. 10 mois payés, 2 offerts), pas un « mensuel × 12 ».
/// </summary>
public class QuotaTierPrice : AuditableEntity
{
    public StudentQuotaTier Tier { get; set; }

    /// <summary>Tarif mensuel standard, FCFA.</summary>
    public decimal MonthlyXof { get; set; }

    /// <summary>Tarif annuel (engagement d'un an), FCFA.</summary>
    public decimal AnnualXof { get; set; }

    /// <summary>Majoration mensuelle quand l'école héberge des élèves (Internat). 0 = aucune.</summary>
    public decimal BoardingSurchargeMonthlyXof { get; set; }

    /// <summary>Majoration annuelle de l'Internat. 0 = aucune.</summary>
    public decimal BoardingSurchargeAnnualXof { get; set; }
}
