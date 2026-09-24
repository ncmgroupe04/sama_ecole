namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Coefficient EFFECTIF d'une matière (Évolution N°4, arbitrage A4) : surcharge de classe, sinon
/// surcharge de série, sinon coefficient de la matière. Pur — aucun accès base.
/// Le primaire neutralise le coefficient à 1 EN AMONT (GetGradeSummaryQueryHandler) : cette fonction
/// n'a pas à le savoir.
/// </summary>
public static class SubjectCoefficients
{
    public static decimal Resolve(decimal baseCoefficient, decimal? classroomOverride, decimal? seriesOverride)
        => classroomOverride ?? seriesOverride ?? baseCoefficient;
}

/// <summary>Surcharges applicables à UN élève pour UNE année : par matière, côté classe et côté série.</summary>
public sealed record CoefficientOverrides(
    IReadOnlyDictionary<Guid, decimal> ByClassroom,
    IReadOnlyDictionary<Guid, decimal> BySeries)
{
    public static readonly CoefficientOverrides None =
        new(new Dictionary<Guid, decimal>(), new Dictionary<Guid, decimal>());

    public decimal Effective(Guid subjectId, decimal baseCoefficient)
        => SubjectCoefficients.Resolve(
            baseCoefficient,
            ByClassroom.TryGetValue(subjectId, out var c) ? c : null,
            BySeries.TryGetValue(subjectId, out var s) ? s : null);
}
