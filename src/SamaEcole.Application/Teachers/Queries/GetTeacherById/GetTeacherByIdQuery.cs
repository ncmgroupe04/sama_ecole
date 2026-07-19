using MediatR;

namespace SamaEcole.Application.Teachers.Queries.GetTeacherById;

/// <summary>
/// GET /api/v1/teachers/{id} — ticket JGK-D04. Fiche complète : matières qualifiées, attributions
/// classe/matière groupées par année scolaire (l'historique demandé — voir TeacherAssignment).
/// </summary>
public record GetTeacherByIdQuery(Guid TeacherId) : IRequest<TeacherProfileDto>;

public record TeacherAssignmentDto(
    Guid Id,
    string ClassroomName,
    string SubjectName,
    Guid SchoolYearId,
    string SchoolYearLabel,
    bool IsActiveSchoolYear);

/// <summary>
/// <see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateTeacherCommand et
/// DeleteTeacherCommand (AGENTS.md règle #5).
/// </summary>
public record TeacherProfileDto(
    Guid Id,
    string Matricule,
    string FullName,
    string Email,
    string? Phone,
    string? BirthPlace,
    string? PhotoUrl,
    string Status,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<TeacherAssignmentDto> Assignments,
    uint RowVersion);
