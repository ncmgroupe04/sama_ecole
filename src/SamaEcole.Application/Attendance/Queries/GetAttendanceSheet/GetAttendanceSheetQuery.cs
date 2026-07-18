using MediatR;

namespace SamaEcole.Application.Attendance.Queries.GetAttendanceSheet;

/// <summary>
/// GET /api/v1/attendance/{id} — ticket JGK-D06 (consultation). Relit une fiche d'appel enregistrée,
/// avec le statut de chaque élève. Réservé au Directeur, au Secrétariat et au Super Admin (Finance
/// exclu — voir AttendanceController). Le tenant vient du JWT : une fiche d'une autre école est
/// introuvable ici.
/// </summary>
public record GetAttendanceSheetQuery(Guid SheetId) : IRequest<AttendanceSheetDto>;

public record AttendanceLineDto(
    Guid StudentId,
    string Matricule,
    string FullName,
    string Status,
    int LateMinutes);

public record AttendanceSheetDto(
    Guid Id,
    Guid ClassroomId,
    string ClassroomName,
    Guid SubjectId,
    string SubjectName,
    Guid SchoolYearId,
    string SchoolYearLabel,
    DateOnly Date,
    string Period,
    IReadOnlyList<AttendanceLineDto> Lines);
