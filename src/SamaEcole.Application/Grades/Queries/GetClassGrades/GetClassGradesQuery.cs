using MediatR;

namespace SamaEcole.Application.Grades.Queries.GetClassGrades;

/// <summary>
/// GET /api/v1/grades?classroomId=&amp;subjectId=&amp;termId= — écran de saisie des notes (Volume 1 §8.1).
///
/// Renvoie les élèves d'une classe avec leurs notes déjà saisies pour une matière et un trimestre
/// donnés (Devoir/Composition), afin que l'enseignant sache en un coup d'œil ce qui reste à saisir
/// sans devoir ouvrir chaque élève un par un. Lecture seule (AGENTS.md règle #7, CQRS) — la saisie et
/// la correction restent CreateGradeCommand/UpdateGradeCommand.
/// </summary>
public record GetClassGradesQuery(Guid ClassroomId, Guid SubjectId, Guid TermId)
    : IRequest<IReadOnlyList<StudentGradeRowDto>>;

/// <summary>
/// Une note déjà saisie. <see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateGradeCommand
/// (AGENTS.md règle #5) : l'écran le transmet tel quel à la correction, sans jamais le relire à part.
/// </summary>
public record GradeCellDto(Guid Id, decimal Value, uint RowVersion);

/// <summary>Une ligne du tableau de saisie. Devoir/Composition sont null tant qu'aucune note n'a été saisie.</summary>
public record StudentGradeRowDto(
    Guid StudentId,
    string Matricule,
    string FullName,
    GradeCellDto? Devoir,
    GradeCellDto? Composition);
