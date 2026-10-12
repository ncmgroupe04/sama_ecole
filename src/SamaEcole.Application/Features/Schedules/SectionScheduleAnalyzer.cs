using SamaEcole.Application.HourVolumes;
using SamaEcole.Application.ReportCards;

namespace SamaEcole.Application.Features.Schedules;

public enum DayHalf
{
    Morning,
    Afternoon
}

/// <summary>
/// Comment les deux sections (française / arabe) se répartissent dans la semaine d'une classe ou d'un enseignant.
/// Constat, jamais un jugement : un établissement franco-arabe organise l'alternance comme il l'entend.
/// </summary>
public enum SectionPattern
{
    /// <summary>Aucun créneau planifié.</summary>
    NoSlots,

    /// <summary>Une seule section est planifiée (classe ou enseignant d'une seule section).</summary>
    SingleSection,

    /// <summary>Chaque jour : le matin en français, l'après-midi en arabe.</summary>
    MorningFrenchAfternoonArabic,

    /// <summary>Chaque jour : le matin en arabe, l'après-midi en français.</summary>
    MorningArabicAfternoonFrench,

    /// <summary>Chaque jour est entièrement d'une seule section, et les deux sections occupent des jours différents.</summary>
    AlternatingDays,

    /// <summary>Tout le reste : demi-journées partagées par les deux sections, ou alternance irrégulière.</summary>
    Mixed
}

/// <summary>Un créneau, réduit à ce que l'analyse d'alternance en lit.</summary>
public sealed record SectionSlot(DayOfWeek Day, TimeOnly Start, TimeOnly End, ReportSection Section);

/// <summary>Heures de chaque section sur UNE demi-journée.</summary>
public sealed record HalfDaySections(DayOfWeek Day, DayHalf Half, decimal FrenchHours, decimal ArabicHours)
{
    /// <summary>Les deux sections se partagent la demi-journée.</summary>
    public bool IsMixed => FrenchHours > 0 && ArabicHours > 0;

    /// <summary>La section de la demi-journée si elle n'en a qu'une, sinon null.</summary>
    public ReportSection? Single => IsMixed ? null : ArabicHours > 0 ? ReportSection.Arabic : FrenchHours > 0 ? ReportSection.French : null;
}

public sealed record SectionScheduleAnalysis(
    decimal FrenchHours,
    decimal ArabicHours,
    IReadOnlyList<HalfDaySections> HalfDays,
    SectionPattern Pattern,
    IReadOnlyList<string> Notices)
{
    /// <summary>Planifie des heures dans les DEUX sections : un enseignant ou une classe « bilingue ».</summary>
    public bool CoversBothSections => FrenchHours > 0 && ArabicHours > 0;
}

/// <summary>
/// Alternance français / arabe d'un emploi du temps — fonction PURE, testée seule. Complète, sans la remplacer, la
/// détection de chevauchement (<see cref="TimetableCompliance.DetectConflicts"/> : un enseignant, une classe ou une
/// salle ne sont jamais à deux endroits à la fois, quelle que soit la section) : ici on ne refuse rien, on DÉCRIT la
/// répartition.
///
/// Une demi-journée se décide à l'heure de DÉBUT du créneau : avant <see cref="AfternoonStart"/> c'est le matin. Un
/// cours qui commence à 11 h 30 et finit à 13 h 30 reste donc un cours du matin — il n'est pas coupé en deux.
/// </summary>
public static class SectionScheduleAnalyzer
{
    /// <summary>Heure de début de l'après-midi (13 h) : l'usage des établissements sénégalais, déjeuner compris.</summary>
    public static readonly TimeOnly AfternoonStart = new(13, 0);

    public static DayHalf HalfOf(TimeOnly start, TimeOnly? afternoonStart = null) =>
        start < (afternoonStart ?? AfternoonStart) ? DayHalf.Morning : DayHalf.Afternoon;

    public static SectionScheduleAnalysis Analyze(IEnumerable<SectionSlot> slots, TimeOnly? afternoonStart = null)
    {
        var list = slots.Where(s => s.End > s.Start).ToList();

        var halfDays = list
            .GroupBy(s => (s.Day, Half: HalfOf(s.Start, afternoonStart)))
            .Select(g => new HalfDaySections(
                g.Key.Day, g.Key.Half,
                g.Where(s => s.Section == ReportSection.French).Sum(s => TimetableCompliance.HoursOf(s.Start, s.End)),
                g.Where(s => s.Section == ReportSection.Arabic).Sum(s => TimetableCompliance.HoursOf(s.Start, s.End))))
            .OrderBy(h => h.Day).ThenBy(h => h.Half)
            .ToList();

        var french = halfDays.Sum(h => h.FrenchHours);
        var arabic = halfDays.Sum(h => h.ArabicHours);

        var notices = halfDays
            .Where(h => h.IsMixed)
            .Select(h => $"{DayName(h.Day)} {(h.Half == DayHalf.Morning ? "matin" : "après-midi")} : les deux sections se partagent la demi-journée.")
            .ToList();

        return new SectionScheduleAnalysis(french, arabic, halfDays, PatternOf(halfDays, french, arabic), notices);
    }

    private static SectionPattern PatternOf(IReadOnlyList<HalfDaySections> halfDays, decimal french, decimal arabic)
    {
        if (halfDays.Count == 0) return SectionPattern.NoSlots;
        if (french == 0 || arabic == 0) return SectionPattern.SingleSection;
        if (halfDays.Any(h => h.IsMixed)) return SectionPattern.Mixed;

        // Toutes les demi-journées sont « pures » (une seule section chacune). Reste à lire comment elles s'enchaînent.
        var days = halfDays.GroupBy(h => h.Day).ToList();

        // Alternance au sein d'une même journée : matin d'une section, après-midi de l'autre — la MÊME chaque jour
        // où les deux demi-journées sont planifiées.
        var splitDays = days.Where(d => d.Count() == 2).ToList();
        var splitSections = splitDays
            .Select(d => (Morning: d.Single(h => h.Half == DayHalf.Morning).Single, Afternoon: d.Single(h => h.Half == DayHalf.Afternoon).Single))
            .ToList();

        var everyDaySplit = days.Count == splitDays.Count;
        if (everyDaySplit && splitSections.All(s => s is { Morning: ReportSection.French, Afternoon: ReportSection.Arabic }))
            return SectionPattern.MorningFrenchAfternoonArabic;
        if (everyDaySplit && splitSections.All(s => s is { Morning: ReportSection.Arabic, Afternoon: ReportSection.French }))
            return SectionPattern.MorningArabicAfternoonFrench;

        // Alternance par JOURNÉES : chaque jour n'a qu'une section, et les deux sections occupent des jours distincts.
        var daySections = days
            .Select(d => d.Select(h => h.Single).Distinct().ToList())
            .ToList();
        if (daySections.All(s => s.Count == 1))
        {
            var distinct = daySections.Select(s => s[0]).Distinct().Count();
            if (distinct == 2) return SectionPattern.AlternatingDays;
        }

        return SectionPattern.Mixed;
    }

    /// <summary>Nom français du jour (la culture du serveur n'est pas celle de l'école : jamais de ToString localisé).</summary>
    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Lundi",
        DayOfWeek.Tuesday => "Mardi",
        DayOfWeek.Wednesday => "Mercredi",
        DayOfWeek.Thursday => "Jeudi",
        DayOfWeek.Friday => "Vendredi",
        DayOfWeek.Saturday => "Samedi",
        _ => "Dimanche"
    };
}
