namespace SamaEcole.Application.Syllabus;

/// <summary>Avancement du programme — pur, testé seul.</summary>
public static class SyllabusCoverage
{
    /// <summary>
    /// Pourcentage (0–100, une décimale) des unités du programme pointées au moins une fois ; null pour un programme
    /// vide (« — », jamais « 0 % » ni « 100 % » d'un programme qui n'existe pas). Une unité pointée plusieurs fois ou
    /// une unité étrangère au programme ne compte pas.
    /// </summary>
    public static decimal? Percent(IReadOnlyCollection<Guid> programmeUnitIds, IEnumerable<Guid> coveredUnitIds)
    {
        if (programmeUnitIds.Count == 0)
        {
            return null;
        }

        var covered = coveredUnitIds.Where(programmeUnitIds.Contains).Distinct().Count();
        return Math.Round(covered * 100m / programmeUnitIds.Count, 1, MidpointRounding.AwayFromZero);
    }
}
