using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Classe d'un établissement (ticket JGK-C02).
///
/// La nomenclature est LIBRE, sans liste figée (openapi.yaml, /classrooms) : « CM2 A » dans une
/// école primaire, « 3e B » dans un collège, « Terminale S2 » dans un lycée. Ne pas introduire
/// d'énumération de niveaux — le Sénégal compte des établissements de tous cycles, et chaque école
/// nomme ses classes comme elle l'entend.
/// </summary>
public class Classroom : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    /// <summary>Cycle ou niveau, en texte libre : « Primaire », « Collège », « Lycée »…</summary>
    public required string Level { get; set; }

    /// <summary>
    /// Cycle d'enseignement structuré, indépendant du <see cref="Level"/> textuel. Pilote le barème de
    /// notation (Primaire /10, Collège &amp; Lycée /20). Valeur par défaut <see cref="CycleType.College"/>.
    /// </summary>
    public CycleType Cycle { get; set; } = CycleType.College;

    /// <summary>Effectif maximal. Sert d'alerte à l'inscription, jamais de blocage dur.</summary>
    public int Capacity { get; set; }
}
