using MediatR;
using SamaEcole.Domain.Enums;

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
    DateOnly BirthDate,
    string? BirthPlace,
    string? Address,

    /// <summary>URL externe BRUTE — round-trip fidèle pour l'édition (voir StudentListItem.PhotoUrl).</summary>
    string? PhotoUrl,

    /// <summary>Feature B — valeur À AFFICHER (voir StudentListItem.PhotoDisplayUrl).</summary>
    string? PhotoDisplayUrl,

    string Status,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<TeacherAssignmentDto> Assignments,
    uint RowVersion,

    // Champs STATEDUC (JGK-M05) — round-trip pour l'édition + affichage sur la fiche. Les enums
    // sérialisent en chaîne (JsonStringEnumConverter global) : « NonRenseigne » tant que rien n'est saisi.
    string? Gender = null,
    AcademicQualification AcademicQualification = AcademicQualification.NonRenseigne,
    ProfessionalQualification ProfessionalQualification = ProfessionalQualification.NonRenseigne,
    TeacherCivilServiceStatus CivilServiceStatus = TeacherCivilServiceStatus.NonRenseigne,
    string? CivilServiceMatricule = null,
    DateOnly? FirstAppointmentDate = null);
