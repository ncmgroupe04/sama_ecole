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

public record TeacherProfileDto(
    Guid Id,
    string Matricule,
    string FullName,
    string Email,
    string? Phone,
    string Status,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<TeacherAssignmentDto> Assignments);
