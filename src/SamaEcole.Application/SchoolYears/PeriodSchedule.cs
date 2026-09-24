using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.SchoolYears;

/// <summary>
/// Découpage d'une année scolaire en périodes d'évaluation — remplace TermSchedule (3 trimestres en
/// dur). Tranches consécutives, sans trou ni chevauchement, la dernière absorbant le reste de la
/// division entière.
///
/// PARTAGÉ, à dessein, par les chemins qui posent ces dates : CreateSchoolYearCommandHandler
/// (crée les lignes), UpdateSchoolYearCommandHandler (recale les dates existantes, via SplitDates,
/// sans jamais recréer — les notes pointent un TermId) et ApplyEvaluationPeriodsCommandHandler.
/// </summary>
public static class PeriodSchedule
{
    public const int MinCustomCount = 2;
    public const int MaxCustomCount = 6;

    public static int CountFor(EvaluationPeriodType type, int customCount) => type switch
    {
        EvaluationPeriodType.Semester => 2,
        EvaluationPeriodType.Custom => customCount,
        _ => 3
    };

    /// <param name="order">Rang de la période, à partir de 1.</param>
    public static string LabelFor(EvaluationPeriodType type, int order)
    {
        var noun = type switch
        {
            EvaluationPeriodType.Semester => "semestre",
            EvaluationPeriodType.Custom => "période",
            _ => "trimestre"
        };

        // « période » est féminin : 1re. « trimestre » / « semestre » : 1er.
        var first = type == EvaluationPeriodType.Custom ? "1re" : "1er";
        var ordinal = order == 1 ? first : $"{order}e";

        return $"{ordinal} {noun}";
    }

    /// <summary>Bornes seules, sans libellé : ce dont le recalage d'une année existante a besoin.</summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> SplitDates(DateOnly start, DateOnly end, int count)
    {
        var chunk = (end.DayNumber - start.DayNumber + 1) / count;

        var periods = new List<(DateOnly Start, DateOnly End)>(count);
        var cursor = start;
        for (var i = 1; i <= count; i++)
        {
            var periodEnd = i == count ? end : cursor.AddDays(chunk - 1); // la dernière absorbe le reste
            periods.Add((cursor, periodEnd));
            cursor = periodEnd.AddDays(1);
        }

        return periods;
    }

    public static IReadOnlyList<(string Label, DateOnly Start, DateOnly End)> Split(
        DateOnly start, DateOnly end, EvaluationPeriodType type, int customCount)
        => SplitDates(start, end, CountFor(type, customCount))
            .Select((p, index) => (LabelFor(type, index + 1), p.Start, p.End))
            .ToList();
}
