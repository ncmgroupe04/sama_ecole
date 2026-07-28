using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Compteur de matricules, par école, par type (élève/enseignant) et par année scolaire —
/// la numérotation est propre à chaque établissement (docs/Volume_1_Cahier_des_Charges.md §2.2).
///
/// Incrémenté par <c>SamaEcole.Persistence.MatriculeGenerator</c> dans la même transaction que
/// l'insertion de l'élève/enseignant (AGENTS.md règle #3) : si l'enregistrement échoue, l'incrément
/// est annulé avec lui — c'est ce qui garantit l'absence de trou dans la numérotation.
/// </summary>
public class MatriculeSequence : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public MatriculeKind Kind { get; set; }

    /// <summary>Année scolaire portée par le matricule (le « 2026 » de <c>ELEV-2026-0001</c>).</summary>
    public int Year { get; set; }

    /// <summary>Dernier numéro attribué pour ce triplet (école, type, année). Le prochain sera <c>LastValue + 1</c>.</summary>
    public int LastValue { get; set; }
}
