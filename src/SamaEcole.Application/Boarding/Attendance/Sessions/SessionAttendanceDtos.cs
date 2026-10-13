using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// Ligne d'une feuille de session (classe ou Halqa). <paramref name="Detail"/> : le régime (« Interne », « Demi-pensionnaire ») quand la
/// session mêle les deux, sinon nul. <paramref name="OnLeave"/> : pensionnaire en sortie ce jour-là, pré-noté « absent — En permission ».
/// </summary>
public record SessionSheetLineDto(
    Guid StudentId,
    string StudentName,
    string? StudentNameAr,
    string Matricule,
    string? Detail,
    bool OnLeave,
    bool Recorded,
    bool? IsPresent,
    string? Note);

/// <summary>
/// Feuille d'appel d'une session pour un groupe (une classe, ou la Halqa d'un Oustaz) et un jour.
/// <paramref name="OutsideSessionDays"/> signale un jour que la session ne couvre pas : information, jamais un blocage.
/// </summary>
public record SessionSheetDto(
    Guid SessionId,
    string SessionName,
    AttendanceAudience Audience,
    Guid GroupId,
    string GroupName,
    DateOnly Date,
    bool OutsideSessionDays,
    IReadOnlyList<SessionSheetLineDto> Lines);

/// <summary>Une ligne envoyée par l'écran de pointage d'une session.</summary>
public record SessionEntryInput(Guid StudentId, bool IsPresent, string? Note);

/// <summary>Résultat d'UNE ligne ; <paramref name="Code"/> d'un rejet : <c>NOT_IN_GROUP</c>, <c>DUPLICATE_ENTRY</c> ou <c>BOARDER_ON_LEAVE</c>.</summary>
public record SessionLineResult(Guid StudentId, string Outcome, string? Code, string? Message);

public record RecordSessionAttendanceResult(
    Guid SessionId, Guid GroupId, DateOnly Date, int Saved, int Rejected, IReadOnlyList<SessionLineResult> Lines);
