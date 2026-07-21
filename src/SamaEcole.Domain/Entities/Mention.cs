using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Mention personnalisable, dérivée automatiquement de la moyenne générale d'un élève (ticket
/// JGK-G02, Volume 1 §8.4). Configurable par le Directeur (docs/Volume_7_Security.md « Paramètres de
/// l'école » : notation, mentions). Seuil TOUJOURS exprimé sur <see cref="MentionScales.Reference"/>
/// (/20) : la mention est une institution du secondaire, et le réglage d'école
/// SchoolSettings.GradingScale ne gouverne plus aucune note depuis le passage au barème par cycle.
/// </summary>
public class Mention : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Ex. « Excellent », « Très Bien »…</summary>
    public required string Label { get; set; }

    /// <summary>Moyenne minimale (incluse) pour obtenir cette mention, sur <see cref="MentionScales.Reference"/>.</summary>
    public decimal MinAverage { get; set; }
}

/// <summary>
/// Barème de référence des seuils de mention, et transposition vers celui d'un bulletin.
///
/// Les mentions sont une institution du SECONDAIRE : leurs seuils sont donc toujours exprimés et
/// stockés sur /20, jamais sur SchoolSettings.GradingScale — ce réglage ne pilote plus aucune note
/// (le barème découle du cycle de la classe, GradingScaleGuard.ScaleForCycle) et une école restée
/// à « 10 » produisait des seuils inatteignables ou triviaux.
///
/// Le bulletin du Primaire porte néanmoins une colonne « Appréciations » par matière, calculée sur
/// des moyennes /10 : ses seuils s'obtiennent en TRANSPOSANT les seuils /20 via <see cref="RescaleTo"/>.
/// Les appliquer tels quels qualifiait « Passable » (seuil 8/20) un élève à 9/10, et ne qualifiait
/// pas du tout un élève à 7,5/10.
/// </summary>
public static class MentionScales
{
    /// <summary>Barème sur lequel tout seuil de mention est saisi, validé et stocké.</summary>
    public const int Reference = 20;

    /// <summary>
    /// Transpose des seuils exprimés sur <see cref="Reference"/> vers le barème d'un bulletin. Le
    /// facteur étant strictement positif, l'ordre décroissant attendu par la sélection de mention
    /// (GradeCalculator.MentionFor) est préservé.
    /// </summary>
    public static IReadOnlyList<(string Label, decimal MinAverage)> RescaleTo(
        IReadOnlyList<(string Label, decimal MinAverage)> mentions, int targetScale) =>
        targetScale == Reference
            ? mentions
            : mentions.Select(m => (m.Label, m.MinAverage * targetScale / Reference)).ToArray();
}

/// <summary>
/// Mentions par défaut d'un établissement neuf, tant que le Directeur n'a rien personnalisé (même
/// principe de « valeurs par défaut appliquées tant que rien n'est stocké » que SchoolSettingsDefaults,
/// voir GetSchoolSettingsQueryHandler). Les seuils restent des FRACTIONS : les appelants les
/// matérialisent sur <see cref="MentionScales.Reference"/>, et <see cref="MentionScales.RescaleTo"/>
/// les transpose ensuite au barème d'un bulletin donné.
/// </summary>
public static class MentionDefaults
{
    private static readonly (string Label, decimal Fraction)[] Scale =
    [
        ("Excellent", 0.80m),
        ("Très Bien", 0.70m),
        ("Bien", 0.60m),
        ("Assez Bien", 0.50m),
        ("Passable", 0.40m)
    ];

    /// <summary>Les mentions par défaut, seuils décimaux calculés sur le barème donné, plus fortes d'abord.</summary>
    public static IReadOnlyList<(string Label, decimal MinAverage)> ForScale(int gradingScale) =>
        Scale.Select(m => (m.Label, m.Fraction * gradingScale)).ToArray();
}
