namespace SamaEcole.Application.Grades;

/// <summary>
/// Calculateur PUR (aucun accès base, aucun effet de bord) de la moyenne pondérée par coefficient
/// (Volume 1 §8.3 : « total des coefficients, total des points, moyenne générale ») et de la mention
/// qui en découle (§8.4). C'est la SEULE formule officielle du domaine — partagée par le recalcul à la
/// demande (JGK-G02, <c>GetGradeSummaryQueryHandler</c>) et par la moyenne d'affichage de la fiche
/// élève (JGK-D02, <c>GetStudentDetailQueryHandler</c>), pour qu'une évolution de la règle de calcul
/// (ex. pondération Devoir/Composition différente de 50/50) ne se fasse jamais qu'à UN seul endroit.
/// </summary>
public static class GradeCalculator
{
    /// <summary>
    /// Moyenne d'une matière : moyenne simple du Devoir et de la Composition présents. La saisie
    /// progresse au fil du trimestre (Volume 1 §8.3) — la moyenne se calcule donc sur ce qui existe,
    /// sans exiger que les deux évaluations soient renseignées.
    /// </summary>
    public static decimal? SubjectAverage(decimal? devoir, decimal? composition) =>
        (devoir, composition) switch
        {
            ({ } d, { } c) => (d + c) / 2m,
            ({ } d, null) => d,
            (null, { } c) => c,
            _ => null
        };

    /// <summary>
    /// Moyenne générale pondérée par les coefficients, sur les seules matières ayant une moyenne (les
    /// autres n'entrent ni dans le total des coefficients ni dans celui des points). Garde-fou anti-
    /// division par zéro : aucune matière notée, ou total des coefficients nul, renvoie un résultat
    /// neutre plutôt qu'une exception.
    /// </summary>
    public static GeneralAverageResult WeightedGeneralAverage(
        IEnumerable<(decimal? Average, decimal Coefficient)> subjects)
    {
        var graded = subjects.Where(s => s.Average is not null).ToList();
        if (graded.Count == 0)
        {
            return new GeneralAverageResult(0m, 0m, null);
        }

        var totalCoefficients = graded.Sum(s => s.Coefficient);
        if (totalCoefficients <= 0)
        {
            return new GeneralAverageResult(totalCoefficients, 0m, null);
        }

        var totalPoints = graded.Sum(s => s.Average!.Value * s.Coefficient);
        return new GeneralAverageResult(totalCoefficients, totalPoints, totalPoints / totalCoefficients);
    }

    /// <summary>
    /// Mention correspondant à une moyenne donnée : le premier seuil atteint dans une liste de mentions
    /// triée par <c>MinAverage</c> décroissant (§8.4) — la plus forte mention atteinte l'emporte.
    /// </summary>
    public static string? MentionFor(decimal average, IReadOnlyList<(string Label, decimal MinAverage)> mentions) =>
        mentions.FirstOrDefault(m => average >= m.MinAverage).Label;
}

/// <summary>
/// Résultat du calcul de moyenne générale : les deux totaux ET la moyenne qui en découle, pour que
/// l'appelant n'ait pas à les recalculer séparément (et risquer une formule légèrement différente).
/// </summary>
public readonly record struct GeneralAverageResult(decimal TotalCoefficients, decimal TotalPoints, decimal? GeneralAverage);
