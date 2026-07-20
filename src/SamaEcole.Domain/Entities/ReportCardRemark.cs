using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Ce que le conseil des professeurs ajoute à la main sur le bulletin d'un élève, pour un trimestre
/// donné : la distinction cochée (Blâme… Félicitations) et le texte d'observations. Au plus UNE ligne
/// par (élève, trimestre) — l'index unique l'impose, comme Grade pour (élève, matière, trimestre, type).
///
/// Délibérément SÉPARÉE de Grade : une distinction/observation n'est pas une note, et une école qui n'a
/// encore rien à en dire pour cet élève ne doit pas avoir de ligne du tout — le bulletin imprime alors
/// les cases vides plutôt qu'une valeur par défaut inventée (même principe que Grade côté notes).
/// </summary>
public class ReportCardRemark : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }
    public Guid TermId { get; set; }

    /// <summary>Null tant qu'aucune distinction n'a été cochée — la ligne s'imprime alors sans coche.</summary>
    public DisciplinaryMention? DisciplinaryMention { get; set; }

    /// <summary>Null tant qu'aucune décision n'a été prise — les trois cases du bloc « Décision du
    /// Conseil » s'impriment alors toutes vides, jamais une décision par défaut inventée.</summary>
    public CouncilDecision? CouncilDecision { get; set; }

    /// <summary>Texte libre du conseil des professeurs. Null/vide : le cadre s'imprime vide.</summary>
    public string? Observations { get; set; }
}
