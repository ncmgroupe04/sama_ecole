using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une ligne du détail des frais d'une inscription (ticket JGK-E01) — le « compte financier »
/// initialisé à l'inscription. C'est un INSTANTANÉ du barème au moment où l'élève est inscrit :
/// <see cref="Designation"/> et <see cref="UnitAmount"/> y sont COPIÉS depuis <see cref="ClassFee"/>,
/// pas référencés. Ainsi le reçu (JGK-E02) restitue exactement ce qui a été facturé, même si le
/// Directeur ajuste le barème l'année suivante (le barème est historisé de son côté, JGK-F01).
///
/// <see cref="Months"/> distingue un frais ponctuel (1) d'une mensualité (le nombre de mensualités
/// par an, réglage <c>TuitionMonthsPerYear</c>) : <see cref="LineTotal"/> = UnitAmount × Months.
/// C'est ici que se matérialise la règle « une mensualité est multipliée par le nombre de mois,
/// jamais un frais ponctuel » (voir <see cref="FeeCategory"/>).
///
/// Pas de verrou optimiste : la ligne est écrite une fois, dans la transaction d'inscription, et
/// n'est jamais rééditée. Une correction de montant passe par le Secrétariat/Admin (règle #4), pas
/// par une modification muette de cet instantané.
/// </summary>
public class EnrollmentFeeLine : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    /// <summary>Catégorie d'origine (traçabilité). Le libellé facturé reste <see cref="Designation"/>, figé.</summary>
    public Guid FeeCategoryId { get; set; }

    /// <summary>Libellé facturé, copié du nom de la catégorie à l'instant de l'inscription.</summary>
    public required string Designation { get; set; }

    /// <summary>Vrai si la ligne provient d'une mensualité — pour l'affichage « × N mois » sur le reçu.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Montant unitaire (une mensualité, ou le frais ponctuel), en FCFA.</summary>
    public decimal UnitAmount { get; set; }

    /// <summary>Nombre de mois facturés : 1 pour un frais ponctuel, TuitionMonthsPerYear pour une mensualité.</summary>
    public int Months { get; set; }

    /// <summary>Total de la ligne = <see cref="UnitAmount"/> × <see cref="Months"/>, en FCFA.</summary>
    public decimal LineTotal { get; set; }
}
