using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance;

public enum DayAttendanceKind { Present, Late, PartialAbsence, FullAbsence }

/// <summary>
/// Classe la JOURNÉE d'un élève d'après ses statuts de séance (Évolution N°5, arbitrage B4). Pur.
/// Calculée, jamais persistée : aucun nouveau statut d'appel. Elle ne porte que sur les séances
/// APPELÉES — l'appelant expose aussi la couverture (« 2 séances appelées sur 5 »), car une seule
/// fiche saisie ne prouve pas une journée.
/// </summary>
public static class DayAttendanceClassifier
{
    public static DayAttendanceKind Classify(IEnumerable<AttendanceStatus> sessionStatuses)
    {
        var statuses = sessionStatuses.ToList();
        var absences = statuses.Count(IsAbsence);

        if (statuses.Count == 0) return DayAttendanceKind.Present;
        if (absences == statuses.Count) return DayAttendanceKind.FullAbsence;
        if (absences > 0) return DayAttendanceKind.PartialAbsence;
        return statuses.Contains(AttendanceStatus.Late) ? DayAttendanceKind.Late : DayAttendanceKind.Present;
    }

    private static bool IsAbsence(AttendanceStatus s)
        => s is AttendanceStatus.JustifiedAbsence or AttendanceStatus.UnjustifiedAbsence;
}
