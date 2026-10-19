using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.RollBook;

public enum DayOffReason
{
    /// <summary>Jour de repos hebdomadaire de l'établissement (<c>SchoolSettings.WorkingDays</c>).</summary>
    RestDay,

    /// <summary>Fermeture du calendrier (jour férié, vacances…).</summary>
    Closure,

    /// <summary>Hors des bornes de l'année scolaire.</summary>
    OutsideYear
}

/// <summary>Un jour du mois sur le cahier : jour de classe, ou jour non travaillé avec sa raison.</summary>
public sealed record ClassDay(
    DateOnly Date,
    bool IsClassDay,
    DayOffReason? OffReason = null,
    SchoolClosure? Closure = null);

/// <summary>
/// Jours de classe d'une période (spec §2.3) : jours de la semaine ouvrés, dans les bornes de l'année,
/// hors fermetures. Pur. Un jour de repos hebdomadaire prime sur une fermeture qui le recouvre (le cahier
/// n'a pas à annoncer « Tabaski » un dimanche) ; hors année prime sur tout.
/// </summary>
public static class RollBookCalendar
{
    public static IReadOnlyList<ClassDay> Build(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<DayOfWeek> workingDays,
        DateOnly yearStart,
        DateOnly yearEnd,
        IEnumerable<SchoolClosure> closures)
    {
        var ordered = closures.OrderBy(c => c.StartDate).ThenBy(c => c.EndDate).ToList();
        var days = new List<ClassDay>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date < yearStart || date > yearEnd)
            {
                days.Add(new ClassDay(date, false, DayOffReason.OutsideYear));
            }
            else if (!workingDays.Contains(date.DayOfWeek))
            {
                days.Add(new ClassDay(date, false, DayOffReason.RestDay));
            }
            else if (ordered.FirstOrDefault(c => c.Covers(date)) is { } closure)
            {
                days.Add(new ClassDay(date, false, DayOffReason.Closure, closure));
            }
            else
            {
                days.Add(new ClassDay(date, true));
            }
        }

        return days;
    }
}
