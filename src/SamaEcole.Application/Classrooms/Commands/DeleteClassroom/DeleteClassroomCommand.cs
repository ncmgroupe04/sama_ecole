using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.DeleteClassroom;

/// <summary>
/// DELETE /api/v1/classrooms/{id} — archive (soft delete) une classe. Toujours un soft delete (AGENTS.md règle #6).
///
/// Spec 2026-10-17 : le Handler refuse la suppression (409) tant qu'un paiement NON ANNULÉ est rattaché à une inscription
/// de la classe (FINANCIAL_RECORDS_EXIST) ou qu'un autre lien existe — élèves rattachés (à transférer ou supprimer),
/// inscriptions, matières de la classe, affectations, emploi du temps… (DEPENDENCIES_EXIST, liste précise). Il ne rompt
/// JAMAIS un lien : c'est UnlinkClassroomDependenciesCommand, demandée explicitement, qui archive ceux qui peuvent l'être.
/// L'historique déjà écrit référence la classe par son Id et affiche « Classe supprimée » (GetStudentDetailQueryHandler).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateClassroomCommand (règle #5).
/// </summary>
public record DeleteClassroomCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
