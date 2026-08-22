using SamaEcole.Domain.Entities;

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
    /// Moyenne des deux devoirs : moyenne simple de Devoir1 et Devoir2 présents (l'un des deux vaut
    /// pour la moyenne si l'autre n'est pas encore saisi). Étape 1 de la moyenne de matière —
    /// <see cref="SubjectAverage"/> moyenne ensuite ce résultat avec la Composition.
    /// </summary>
    public static decimal? DevoirAverage(decimal? devoir1, decimal? devoir2) =>
        AverageIfPresent(devoir1, devoir2);

    /// <summary>
    /// Moyenne d'une matière : moyenne des devoirs (<see cref="DevoirAverage"/>), puis moyenne simple
    /// de ce résultat avec la Composition. La saisie progresse au fil du trimestre (Volume 1 §8.3) —
    /// la moyenne se calcule donc sur ce qui existe, sans exiger que les trois évaluations soient
    /// renseignées.
    /// </summary>
    public static decimal? SubjectAverage(decimal? devoir1, decimal? devoir2, decimal? composition) =>
        AverageIfPresent(DevoirAverage(devoir1, devoir2), composition);

    /// <summary>Moyenne simple de deux valeurs optionnelles — l'une des deux vaut pour la moyenne si l'autre est absente.</summary>
    private static decimal? AverageIfPresent(decimal? a, decimal? b) =>
        (a, b) switch
        {
            ({ } x, { } y) => (x + y) / 2m,
            ({ } x, null) => x,
            (null, { } y) => y,
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

    /// <summary>
    /// Barème réellement applicable à une LIGNE d'évaluation : celui que l'école a fixé sur la matière
    /// (<see cref="Subject.MaxScore"/> — 10, 16, 24, 40, 60… des grilles APC du primaire) et, à défaut,
    /// celui du cycle de la classe (<see cref="GradingScaleGuard.ScaleForCycle"/>).
    ///
    /// Le repli n'est pas un détail : <c>MaxScore</c> est NULL sur toutes les matières antérieures à
    /// cette option, et une valeur par défaut fixe (20) y aurait discrètement relevé le plafond du
    /// primaire de /10 à /20. Le null dit « suis le cycle », pas « pas de barème ».
    ///
    /// Une valeur nulle ou négative en base serait absurde (division par zéro dans
    /// <see cref="Rebase"/>) : le repli la traite comme absente plutôt que de propager l'aberration
    /// jusqu'au bulletin.
    /// </summary>
    public static decimal EffectiveMaxScore(decimal? subjectMaxScore, int cycleScale)
        => subjectMaxScore is { } max && max > 0 ? max : cycleScale;

    /// <summary>
    /// Ramène une note à un barème cible : <c>note ÷ barème d'origine × barème cible</c>. C'est ce qui
    /// rend comparables les lignes d'une grille APC, où 45/60 et 18/24 valent tous deux 15/20 — les
    /// moyenner brutes ferait peser une ligne notée sur 60 trois fois plus qu'une ligne sur 20, sans que
    /// l'école ne l'ait jamais demandé (le coefficient, lui, est déclaré).
    ///
    /// Barème d'origine nul ou négatif → la note est rendue TELLE QUELLE plutôt que de diviser par zéro.
    /// </summary>
    public static decimal Rebase(decimal value, decimal fromScale, decimal toScale) =>
        fromScale > 0 ? value * toScale / fromScale : value;

    /// <summary>
    /// Appréciation d'une LIGNE d'évaluation d'après son pourcentage de réussite (<c>note ÷ barème</c>),
    /// sur l'échelle de mentions de l'école — exprimée, elle, sur <see cref="MentionScales.Reference"/>
    /// (/20). Passer par le pourcentage est ce qui permet à une même échelle de qualifier « Excellent »
    /// un 10/10 comme un 48/60, sans dupliquer un barème d'appréciations par valeur de « Sur ».
    ///
    /// Null quand rien n'est encore noté : la case du bulletin reste vide, jamais une appréciation
    /// attribuée à une note absente.
    /// </summary>
    public static string? AppreciationFor(
        decimal? score, decimal maxScore, IReadOnlyList<(string Label, decimal MinAverage)> mentionsOnReferenceScale) =>
        score is { } value
            ? MentionFor(Rebase(value, maxScore, MentionScales.Reference), mentionsOnReferenceScale)
            : null;
}

/// <summary>
/// Résultat du calcul de moyenne générale : les deux totaux ET la moyenne qui en découle, pour que
/// l'appelant n'ait pas à les recalculer séparément (et risquer une formule légèrement différente).
/// </summary>
public readonly record struct GeneralAverageResult(decimal TotalCoefficients, decimal TotalPoints, decimal? GeneralAverage);
