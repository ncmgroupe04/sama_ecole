using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Mention personnalisable, dérivée automatiquement de la moyenne générale d'un élève (ticket
/// JGK-G02, Volume 1 §8.4). Configurable par le Directeur (docs/Volume_7_Security.md « Paramètres de
/// l'école » : notation, mentions). Seuil exprimé SUR LE BARÈME de l'école (10 ou 20) — deux écoles au
/// barème différent peuvent donc porter la même mention à des seuils numériques différents.
/// </summary>
public class Mention : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Ex. « Excellent », « Très Bien »…</summary>
    public required string Label { get; set; }

    /// <summary>Moyenne minimale (incluse) pour obtenir cette mention, sur le barème de l'école.</summary>
    public decimal MinAverage { get; set; }
}

/// <summary>
/// Mentions par défaut d'un établissement neuf, tant que le Directeur n'a rien personnalisé (même
/// principe de « valeurs par défaut appliquées tant que rien n'est stocké » que SchoolSettingsDefaults,
/// voir GetSchoolSettingsQueryHandler). Les seuils sont des FRACTIONS du barème de l'école, pas des
/// valeurs figées sur 20 : ils s'adaptent donc automatiquement qu'une école note sur 10 ou sur 20.
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
