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

    /// <summary>
    /// Classe PASSERELLE / ACCÉLÉRÉE : une seule année scolaire valide DEUX niveaux successifs
    /// (« CI-CP » au primaire, « 6e-5e » pour les filières d'intégration des daaras coraniques).
    /// OPTIONNEL et faux par défaut — une classe ordinaire ne change en rien de comportement.
    /// </summary>
    public bool IsAccelerated { get; set; }

    /// <summary>
    /// SECOND niveau validé en fin d'année par une classe accélérée (« CP » pour une classe « CI-CP »),
    /// en toutes lettres : « CP », « CM2 », « Cinquième », « Terminale »…
    ///
    /// Pourquoi un LIBELLÉ et pas un identifiant de niveau : il n'existe aucune table de niveaux dans ce
    /// schéma, et c'est délibéré (AGENTS.md, Classroom.Level : « ne pas introduire d'énumération de
    /// niveaux »). Le niveau d'une classe se lit sur son <see cref="Name"/> ; ce champ suit exactement la
    /// même nomenclature (voir <c>ClassroomGradeLevels</c>), pour que les deux niveaux validés se
    /// comparent et s'impriment sans table de correspondance.
    ///
    /// Null pour toute classe non accélérée : les deux champs sont tenus cohérents à l'écriture
    /// (CreateClassroomCommandHandler / UpdateClassroomCommandHandler), jamais un niveau cible orphelin.
    /// </summary>
    public string? TargetLevel { get; set; }

    /// <summary>
    /// Série du lycée (Évolution N°4) : code du catalogue fermé <c>LyceeSeries</c> (S1, S2, L1a, L2, STEG, LA… — voir LyceeSeries).
    /// Null pour toute classe sans série — Seconde commune, et TOUTE classe hors lycée, où la série n'a
    /// pas de sens (refusée à l'écriture). L'élève hérite la série de sa classe (arbitrage A1) : c'est la
    /// clé des coefficients par série (SubjectCoefficientOverride).
    /// </summary>
    public string? Series { get; set; }
}
