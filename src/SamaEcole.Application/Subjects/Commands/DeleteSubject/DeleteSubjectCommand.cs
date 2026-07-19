using MediatR;

namespace SamaEcole.Application.Subjects.Commands.DeleteSubject;

/// <summary>
/// DELETE /api/v1/subjects/{id} — archive (soft delete) une matière créée par erreur. Toujours un soft
/// delete (AGENTS.md règle #6). Même permission que la modification (GradingPolicies.CanManageGradingScale).
///
/// Le Handler DOIT rejeter la suppression si des notes existent déjà pour cette matière (voir
/// DeleteSubjectCommandHandler) : au-delà de la matière elle-même, c'est tout le calcul des moyennes
/// et des bulletins qui dépend de son coefficient (Subject.Coefficient).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateSubjectCommand (règle #5).
/// </summary>
public record DeleteSubjectCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
