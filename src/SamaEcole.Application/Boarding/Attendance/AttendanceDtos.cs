namespace SamaEcole.Application.Boarding.Attendance;

/// <summary>Une ligne de la feuille d'appel envoyée par l'écran de pointage.</summary>
public record AttendanceEntryInput(Guid BoarderId, bool IsPresent, string? Note);

/// <summary>
/// Résultat d'UNE ligne du pointage collectif. <see cref="Outcome"/> : <c>Saved</c> ou <c>Rejected</c> ; pour un rejet,
/// <see cref="Code"/> vaut <c>NOT_IN_DORMITORY</c>, <c>DUPLICATE_ENTRY</c> ou <c>BOARDER_ON_LEAVE</c>.
/// </summary>
public record AttendanceLineResult(Guid BoarderId, string Outcome, string? Code, string? Message);

public record RecordAttendanceResult(
    DateOnly Date, Guid DormitoryId, int Saved, int Rejected, IReadOnlyList<AttendanceLineResult> Lines, Guid? SessionId = null);

/// <summary>
/// Ligne de la feuille d'appel d'un pavillon. <see cref="IsPresent"/> est <c>null</c> tant que rien n'est saisi ; un pensionnaire
/// en sortie ce soir-là est pré-rempli « absent — En permission » (<see cref="OnLeave"/>).
/// </summary>
public record AttendanceSheetLineDto(
    Guid BoarderId,
    string StudentName,
    string Matricule,
    string RoomName,
    int BedNumber,
    bool OnLeave,
    bool Recorded,
    bool? IsPresent,
    string? Note);

/// <summary>
/// Feuille d'appel d'un pavillon. <paramref name="SessionId"/> / <paramref name="SessionName"/> : la session pointée (nulle tant que
/// l'école n'en a aucune) ; <paramref name="OutsideSessionDays"/> signale un jour que la session ne couvre pas — information, pas un blocage.
/// </summary>
public record AttendanceSheetDto(
    DateOnly Date,
    Guid DormitoryId,
    string DormitoryName,
    IReadOnlyList<AttendanceSheetLineDto> Lines,
    Guid? SessionId = null,
    string? SessionName = null,
    bool OutsideSessionDays = false);
