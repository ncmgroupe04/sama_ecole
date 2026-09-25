using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Matière au programme d'UNE classe (Évolution N°6 — séries, matières et options) : la liste que l'onglet
/// « Matières par classe » affiche et que le bulletin respecte. Injectée depuis le modèle national de la série
/// à la création de la classe (ClassSubjectTemplateInjector), puis ajustée par le Directeur : matière propre à
/// l'établissement (<see cref="IsCustom"/>), matière désactivée (<see cref="IsActive"/>), groupe d'options.
///
/// Elle ne porte PAS de coefficient : le coefficient d'une matière pour une classe reste celui du moteur de
/// l'Évolution N°4 (surcharge de classe › de série › coefficient de la matière, par année scolaire —
/// arbitrage A6). Une seconde valeur ici créerait deux vérités pour le même bulletin.
///
/// Une classe qui n'a AUCUNE ligne ici se comporte exactement comme avant : l'élève suit toute matière notée.
/// Une matière notée qui n'a pas de ligne ici reste, elle aussi, suivie (voir SubjectFollowRules).
/// </summary>
public class ClassSubject : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ClassroomId { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>
    /// Groupe d'options (« LV2 », « Option scientifique ») : les matières d'un même groupe sont des alternatives,
    /// l'élève en suit UNE, choisie à l'inscription (<see cref="StudentSubjectEnrollment"/>). Null : matière
    /// suivie par toute la classe.
    /// </summary>
    public string? OptionGroup { get; set; }

    /// <summary>Vrai pour une matière ajoutée par le Directeur (Informatique, Conduite…), hors modèle national.</summary>
    public bool IsCustom { get; set; }

    /// <summary>
    /// Faux : matière désactivée pour cette classe — absente de la grille de saisie et du bulletin, sans rien
    /// effacer (les notes déjà saisies restent en base, règle #6).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Rang d'affichage : l'ordre du modèle national, puis les matières ajoutées.</summary>
    public int DisplayOrder { get; set; }
}
