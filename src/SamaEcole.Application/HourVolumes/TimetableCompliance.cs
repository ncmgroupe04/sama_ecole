using SamaEcole.Application.Coefficients;

namespace SamaEcole.Application.HourVolumes;

/// <summary>Écart d'une matière (ou d'un groupe d'options) à son volume de référence.</summary>
public enum HourComplianceStatus
{
    /// <summary>Aucun volume de référence : ni grille nationale codée, ni réglage de l'école.</summary>
    NoReference,
    Compliant,
    Under,
    Over
}

/// <summary>Une matière de la classe, telle que la voit le contrôle : ses heures planifiées et sa référence.</summary>
public sealed record ComplianceInput(Guid SubjectId, string SubjectName, decimal PlannedHours, decimal? NormHours, string? OptionGroup);

public sealed record SubjectCompliance(
    Guid? SubjectId, string SubjectName, string? OptionGroup, decimal PlannedHours, decimal? NormHours, decimal? Difference,
    HourComplianceStatus Status);

public sealed record ComplianceTotals(decimal PlannedHours, decimal? NormHours, decimal? Difference, HourComplianceStatus Status);

/// <summary>Chevauchement de deux créneaux : même enseignant, même salle ou même classe, le même jour, aux mêmes heures.</summary>
public enum ScheduleConflictKind
{
    Teacher,
    Room,
    Classroom
}

/// <summary>Un créneau, réduit à ce qu'il faut pour détecter et décrire un chevauchement.</summary>
public sealed record SlotInfo(
    Guid Id, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime,
    Guid TeacherId, string TeacherName, Guid ClassroomId, string ClassroomName, Guid SubjectId, string SubjectName, string? RoomNumber);

public sealed record ScheduleConflict(ScheduleConflictKind Kind, DayOfWeek DayOfWeek, string Resource, SlotInfo First, SlotInfo Second);

/// <summary>
/// Conformité d'un emploi du temps (Évolution N°7) — pur, testé seul.
///
/// Règles :
/// - une matière planifiée se compare à sa référence : égale (à la minute près) → conforme, en deçà → sous le volume,
///   au-delà → au-dessus ; sans référence → « sans référence », jamais « conforme » ;
/// - une matière de référence non planifiée est « sous le volume » (0 h) ;
/// - un GROUPE d'options (LV2, langue ancienne) : l'élève n'en suit qu'une, donc les langues non planifiées du groupe
///   sont ignorées dès qu'une l'est ; si aucune ne l'est, le groupe ressort en UNE ligne manquante ;
/// - le total de la classe additionne les matières hors groupe et, par groupe, sa plus forte référence.
/// </summary>
public static class TimetableCompliance
{
    /// <summary>Tolérance d'arrondi : une minute.</summary>
    private const decimal Tolerance = 1m / 60m;

    public static HourComplianceStatus StatusOf(decimal planned, decimal? norm)
    {
        if (norm is not { } n) return HourComplianceStatus.NoReference;
        if (Math.Abs(planned - n) <= Tolerance) return HourComplianceStatus.Compliant;
        return planned < n ? HourComplianceStatus.Under : HourComplianceStatus.Over;
    }

    public static IReadOnlyList<SubjectCompliance> Evaluate(IEnumerable<ComplianceInput> inputs)
    {
        var list = inputs.ToList();
        var rows = new List<SubjectCompliance>();

        foreach (var input in list.Where(i => i.OptionGroup is null))
        {
            if (input.PlannedHours <= 0 && (input.NormHours ?? 0) <= 0) continue;
            rows.Add(Row(input.SubjectId, input.SubjectName, null, input.PlannedHours, input.NormHours));
        }

        foreach (var group in list.Where(i => i.OptionGroup is not null).GroupBy(i => i.OptionGroup!))
        {
            var planned = group.Where(i => i.PlannedHours > 0).ToList();
            if (planned.Count > 0)
            {
                rows.AddRange(planned.Select(i => Row(i.SubjectId, i.SubjectName, group.Key, i.PlannedHours, i.NormHours)));
            }
            else if (group.Max(i => i.NormHours) is { } norm && norm > 0)
            {
                rows.Add(Row(null, $"{group.Key} (au choix)", group.Key, 0m, norm));
            }
        }

        return rows
            .OrderBy(r => r.Status == HourComplianceStatus.Compliant ? 1 : 0)
            .ThenBy(r => r.SubjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static ComplianceTotals Totals(IReadOnlyCollection<ComplianceInput> inputs)
    {
        var planned = inputs.Sum(i => i.PlannedHours);

        var norms = inputs.Where(i => i.OptionGroup is null && i.NormHours is not null).Select(i => i.NormHours!.Value)
            .Concat(inputs.Where(i => i.OptionGroup is not null && i.NormHours is not null)
                .GroupBy(i => i.OptionGroup!).Select(g => g.Max(i => i.NormHours!.Value)))
            .ToList();

        decimal? norm = norms.Count == 0 ? null : norms.Sum();
        return new ComplianceTotals(planned, norm, norm is null ? null : planned - norm, StatusOf(planned, norm));
    }

    private static SubjectCompliance Row(Guid? subjectId, string name, string? group, decimal planned, decimal? norm)
        => new(subjectId, name, group, planned, norm, norm is null ? null : planned - norm, StatusOf(planned, norm));

    /// <summary>Durée d'un créneau, en heures.</summary>
    public static decimal HoursOf(TimeOnly start, TimeOnly end)
        => end <= start ? 0m : (decimal)(end - start).TotalMinutes / 60m;

    /// <summary>
    /// Tous les chevauchements d'un ensemble de créneaux, une ligne par paire et par ressource partagée (enseignant,
    /// salle — nom comparé sans casse, accents ni ponctuation — ou classe). Deux créneaux qui se touchent (fin = début)
    /// ne se chevauchent pas.
    /// </summary>
    public static IReadOnlyList<ScheduleConflict> DetectConflicts(IEnumerable<SlotInfo> slots)
    {
        var conflicts = new List<ScheduleConflict>();

        foreach (var day in slots.GroupBy(s => s.DayOfWeek))
        {
            var ordered = day.OrderBy(s => s.StartTime).ThenBy(s => s.Id).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count && ordered[j].StartTime < ordered[i].EndTime; j++)
                {
                    var a = ordered[i];
                    var b = ordered[j];

                    if (a.TeacherId == b.TeacherId)
                        conflicts.Add(new ScheduleConflict(ScheduleConflictKind.Teacher, day.Key, a.TeacherName, a, b));

                    if (RoomKey(a.RoomNumber) is { } room && room == RoomKey(b.RoomNumber))
                        conflicts.Add(new ScheduleConflict(ScheduleConflictKind.Room, day.Key, a.RoomNumber!.Trim(), a, b));

                    if (a.ClassroomId == b.ClassroomId)
                        conflicts.Add(new ScheduleConflict(ScheduleConflictKind.Classroom, day.Key, a.ClassroomName, a, b));
                }
            }
        }

        return conflicts
            .OrderBy(c => ((int)c.DayOfWeek + 6) % 7) // lundi d'abord
            .ThenBy(c => c.First.StartTime)
            .ThenBy(c => c.Kind)
            .ToList();
    }

    /// <summary>Forme de comparaison d'une salle (« Salle 12 », « salle-12 » se valent) ; null si vide.</summary>
    public static string? RoomKey(string? room)
    {
        var key = SeriesCoefficientTemplates.NormalizeName(room);
        return key.Length == 0 ? null : key;
    }
}
