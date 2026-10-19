using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.RollBook;

/// <summary>
/// Marque d'une case du cahier de roulement (un élève, une demi-journée). Les quatre statuts d'appel
/// existants, plus deux constats qui ne se SAISISSENT pas : <see cref="NotRecorded"/> (rien n'a été saisi
/// ni appelé) et <see cref="PartialAbsence"/> (absent à une partie seulement des séances de la demi-journée,
/// dérivé des appels par séance).
/// </summary>
public enum RollBookMark
{
    NotRecorded = 0,
    Present = 1,
    Late = 2,
    PartialAbsence = 3,
    JustifiedAbsence = 4,
    UnjustifiedAbsence = 5
}

/// <summary>D'où vient la marque : saisie du cahier (prioritaire), calcul à partir des appels, ou rien.</summary>
public enum RollBookSource
{
    None = 0,
    Derived = 1,
    Entry = 2
}

/// <summary>Une case résolue. <see cref="EntryId"/> n'est renseigné que pour une saisie du cahier.</summary>
public sealed record RollBookCell(
    RollBookMark Mark,
    RollBookSource Source,
    int LateMinutes = 0,
    string? Reason = null,
    Guid? EntryId = null)
{
    public static readonly RollBookCell Empty = new(RollBookMark.NotRecorded, RollBookSource.None);

    public bool IsRecorded => Mark != RollBookMark.NotRecorded;
}

public static class RollBookMarks
{
    public static RollBookMark MarkOf(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => RollBookMark.Present,
        AttendanceStatus.Late => RollBookMark.Late,
        AttendanceStatus.JustifiedAbsence => RollBookMark.JustifiedAbsence,
        AttendanceStatus.UnjustifiedAbsence => RollBookMark.UnjustifiedAbsence,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Statut d'appel inconnu.")
    };

    /// <summary>Le statut d'appel saisissable correspondant, ou null pour les constats non saisissables.</summary>
    public static AttendanceStatus? StatusOf(RollBookMark mark) => mark switch
    {
        RollBookMark.Present => AttendanceStatus.Present,
        RollBookMark.Late => AttendanceStatus.Late,
        RollBookMark.JustifiedAbsence => AttendanceStatus.JustifiedAbsence,
        RollBookMark.UnjustifiedAbsence => AttendanceStatus.UnjustifiedAbsence,
        _ => null
    };
}

/// <summary>Un statut d'appel de séance rattaché à une demi-journée (entrée de <see cref="HalfDayDeriver"/>).</summary>
public sealed record SessionLine(AttendanceStatus Status, int LateMinutes);

/// <summary>
/// Statut DÉRIVÉ d'une demi-journée à partir des appels par séance (spec §2.2). Pur : réutilise
/// <see cref="Attendance.DayAttendanceClassifier"/> sur le sous-ensemble de séances de la demi-journée.
/// </summary>
public static class HalfDayDeriver
{
    public static RollBookCell Derive(IReadOnlyCollection<SessionLine> sessions)
    {
        if (sessions.Count == 0)
        {
            return RollBookCell.Empty;
        }

        var kind = Attendance.DayAttendanceClassifier.Classify(sessions.Select(s => s.Status));

        return kind switch
        {
            Attendance.DayAttendanceKind.Present => new RollBookCell(RollBookMark.Present, RollBookSource.Derived),
            Attendance.DayAttendanceKind.Late => new RollBookCell(
                RollBookMark.Late, RollBookSource.Derived, sessions.Max(s => s.LateMinutes)),
            Attendance.DayAttendanceKind.PartialAbsence => new RollBookCell(RollBookMark.PartialAbsence, RollBookSource.Derived),
            // Absence de toute la demi-journée : justifiée seulement si CHAQUE séance l'est — une seule
            // séance non justifiée suffit à ne pas excuser la demi-journée.
            _ => new RollBookCell(
                sessions.All(s => s.Status == AttendanceStatus.JustifiedAbsence)
                    ? RollBookMark.JustifiedAbsence
                    : RollBookMark.UnjustifiedAbsence,
                RollBookSource.Derived)
        };
    }
}

/// <summary>Fusion saisie du cahier / statut dérivé : la saisie prime, sinon le calcul, sinon rien.</summary>
public static class RollBookCellResolver
{
    public static RollBookCell Resolve(RollBookEntry? entry, RollBookCell derived) =>
        entry is null
            ? derived
            : new RollBookCell(
                RollBookMarks.MarkOf(entry.Status), RollBookSource.Entry, entry.LateMinutes, entry.Reason, entry.Id);
}
