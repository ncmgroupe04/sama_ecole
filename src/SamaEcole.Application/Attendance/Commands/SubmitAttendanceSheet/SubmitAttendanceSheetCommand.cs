using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;

/// <summary>
/// POST /api/v1/attendance — ticket JGK-D06. Enregistre l'appel complet : la fiche + le statut de
/// chaque élève, dans une seule transaction.
///
/// Ni SchoolId ni SchoolYearId dans la requête : l'école vient du JWT et l'année ACTIVE est résolue
/// serveur (AGENTS.md règle #10, même convention que Enrollment/TeacherAssignment). Une seconde saisie
/// de la même clé (classe, matière, date, créneau) est refusée en 409.
/// </summary>
public record SubmitAttendanceSheetCommand : IRequest<SubmitAttendanceSheetResult>
{
    public required Guid ClassroomId { get; init; }
    public required Guid SubjectId { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>
    /// Créneau en texte libre (« Matin », « 1re heure »…) — mode LIBRE. Ignoré (et non exigé) quand
    /// <see cref="ScheduleSlotId"/> est fourni : le serveur le dérive alors des horaires du cours.
    /// </summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>
    /// Cours d'emploi du temps sur lequel l'appel est fait (Évolution N°5). Optionnel : absent, l'appel est
    /// « libre » et se comporte exactement comme avant.
    /// </summary>
    public Guid? ScheduleSlotId { get; init; }

    public required IReadOnlyList<AttendanceEntry> Entries { get; init; }
}

/// <summary>Statut d'un élève à l'appel. LateMinutes n'est lu que si Status vaut Late.</summary>
public record AttendanceEntry(Guid StudentId, AttendanceStatus Status, int LateMinutes);

public record SubmitAttendanceSheetResult(Guid Id, int StudentCount);
