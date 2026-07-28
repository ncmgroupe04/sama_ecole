using MediatR;

namespace SamaEcole.Application.Teachers.Commands.AssignTeacher;

/// <summary>
/// POST /api/v1/teachers/{id}/assignments — ticket JGK-D04. Attribue un enseignant à une classe pour
/// une matière, sur l'année scolaire ACTIVE.
///
/// Pas de SchoolYearId ici, et c'est délibéré (même convention que CreateEnrollmentCommand) : l'année
/// est résolue serveur, jamais choisie par le client (AGENTS.md règle #10). C'est aussi ce qui
/// transforme la table en historique — une attribution reste rattachée à l'année qui était active au
/// moment où elle a été posée, même après le basculement sur l'année suivante.
/// </summary>
public record AssignTeacherCommand : IRequest<AssignTeacherResult>
{
    /// <summary>Renseigné par le contrôleur depuis le segment {id} de l'URL, jamais par le client.</summary>
    public Guid TeacherId { get; init; }

    public required Guid ClassroomId { get; init; }
    public required Guid SubjectId { get; init; }
}

public record AssignTeacherResult(Guid Id, Guid ClassroomId, Guid SubjectId, Guid SchoolYearId);
