using MediatR;

namespace SamaEcole.Application.Subjects.Commands.DeleteSubject;

/// <summary>
/// DELETE /api/v1/subjects/{id} — archive (soft delete) une matière. Toujours un soft delete (AGENTS.md règle #6).
/// Même permission que la modification (GradingPolicies.CanManageGradingScale).
///
/// Spec 2026-10-17 : le Handler refuse la suppression (409 DEPENDENCIES_EXIST, liste précise) tant qu'un lien existe —
/// notes, classes, enseignants, emploi du temps…, et les activités d'un domaine, qui ne se rompent pas en un clic. Il ne
/// rompt JAMAIS un lien : c'est UnlinkSubjectDependenciesCommand, demandée explicitement, qui archive ceux qui peuvent
/// l'être (les notes sont archivées après confirmation, leur nombre affiché).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateSubjectCommand (règle #5).
/// </summary>
public record DeleteSubjectCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
