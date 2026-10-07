namespace SamaEcole.Application.Boarding.Attendance;

/// <summary>Une ligne de la feuille d'appel envoyée par l'écran de pointage.</summary>
public record AttendanceEntryInput(Guid BoarderId, bool IsPresent, string? Note);

/// <summary>
/// Résultat d'UNE ligne du pointage collectif. <see cref="Outcome"/> : <c>Saved</c> ou <c>Rejected</c> ; pour un rejet,
/// <see cref="Code"/> vaut <c>NOT_IN_DORMITORY</c>, <c>DUPLICATE_ENTRY</c> ou <c>BOARDER_ON_LEAVE</c>.
/// </summary>
public record AttendanceLineResult(Guid BoarderId, string Outcome, string? Code, string? Message);

public record RecordAttendanceResult(
    DateOnly Date, Guid DormitoryId, int Saved, int Rejected, IReadOnlyList<AttendanceLineResult> Lines);

/// <summary>
/// Ligne de la feuille d'appel du soir. <see cref="IsPresent"/> est <c>null</c> tant que rien n'est saisi ; un pensionnaire
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

public record AttendanceSheetDto(DateOnly Date, Guid DormitoryId, string DormitoryName, IReadOnlyList<AttendanceSheetLineDto> Lines);
