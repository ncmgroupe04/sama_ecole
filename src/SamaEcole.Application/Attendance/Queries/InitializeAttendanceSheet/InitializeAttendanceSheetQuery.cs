using MediatR;

namespace SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;

/// <summary>
/// GET /api/v1/attendance/roster — ticket JGK-D06. Génère la grille d'appel : les élèves d'une classe
/// pour une date, une matière et un créneau donnés, afin que l'enseignant coche les statuts.
///
/// Si une fiche existe DÉJÀ pour cette clé (classe, matière, date, créneau), les statuts déjà saisis
/// sont renvoyés avec chaque élève — l'écran affiche l'appel existant plutôt qu'une grille vierge, et
/// signale (AlreadySubmitted) qu'une nouvelle soumission serait un doublon (409).
/// </summary>
public record InitializeAttendanceSheetQuery : IRequest<AttendanceRosterDto>
{
    public required Guid ClassroomId { get; init; }
    public required Guid SubjectId { get; init; }
    public required DateOnly Date { get; init; }
    public required string Period { get; init; }
}

public record AttendanceRosterRow(
    Guid StudentId,
    string Matricule,
    string FullName,
    string? Status,
    int LateMinutes);

public record AttendanceRosterDto(
    Guid ClassroomId,
    string ClassroomName,
    Guid SubjectId,
    string SubjectName,
    DateOnly Date,
    string Period,
    bool AlreadySubmitted,
    IReadOnlyList<AttendanceRosterRow> Students);
