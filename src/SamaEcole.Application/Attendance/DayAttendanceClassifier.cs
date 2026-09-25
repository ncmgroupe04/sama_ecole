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

        return Classify(
            statuses.Count,
            statuses.Count(IsAbsence),
            statuses.Count(s => s == AttendanceStatus.Late));
    }

    /// <summary>
    /// Même règle sur des COMPTEURS : c'est la forme qu'utilise le rapport, qui agrège par jour côté base plutôt
    /// que de ramener toutes les lignes d'appel en mémoire.
    /// </summary>
    public static DayAttendanceKind Classify(int sessions, int absences, int lates)
    {
        if (sessions == 0) return DayAttendanceKind.Present;
        if (absences == sessions) return DayAttendanceKind.FullAbsence;
        if (absences > 0) return DayAttendanceKind.PartialAbsence;
        return lates > 0 ? DayAttendanceKind.Late : DayAttendanceKind.Present;
    }

    private static bool IsAbsence(AttendanceStatus s)
        => s is AttendanceStatus.JustifiedAbsence or AttendanceStatus.UnjustifiedAbsence;
}
