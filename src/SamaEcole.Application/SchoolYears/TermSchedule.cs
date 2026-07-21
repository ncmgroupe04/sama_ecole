namespace SamaEcole.Application.SchoolYears;

/// <summary>
/// Découpage d'une année scolaire en trimestres (ticket JGK-G01) — trois tranches consécutives, sans
/// trou ni chevauchement, la dernière absorbant le reste de la division entière.
///
/// PARTAGÉ, à dessein, par les deux seuls chemins qui posent ces dates :
///   * CreateSchoolYearCommandHandler, qui CRÉE les lignes de trimestres ;
///   * UpdateSchoolYearCommandHandler, qui RECALE les lignes existantes quand les dates de l'année
///     changent — sans jamais les recréer : les notes pointent un TermId, supprimer un trimestre les
///     orphelinerait (et la FK Restrict le refuserait de toute façon).
///
/// Dupliquer ce calcul ferait diverger les deux chemins au premier ajustement : une année modifiée
/// n'aurait alors plus le même découpage qu'une année créée avec les mêmes dates.
/// </summary>
internal static class TermSchedule
{
    /// <summary>Le système sénégalais standard compte trois trimestres — aucun écran ne les configure.</summary>
    public const int TermCount = 3;

    public static IReadOnlyList<(string Label, DateOnly Start, DateOnly End)> Split(DateOnly start, DateOnly end)
    {
        var totalDays = end.DayNumber - start.DayNumber + 1;
        var chunk = totalDays / TermCount;

        var firstEnd = start.AddDays(chunk - 1);
        var secondStart = firstEnd.AddDays(1);
        var secondEnd = secondStart.AddDays(chunk - 1);
        var thirdStart = secondEnd.AddDays(1);

        return
        [
            ("1er trimestre", start, firstEnd),
            ("2e trimestre", secondStart, secondEnd),
            ("3e trimestre", thirdStart, end) // absorbe le reste de la division entière
        ];
    }
}
