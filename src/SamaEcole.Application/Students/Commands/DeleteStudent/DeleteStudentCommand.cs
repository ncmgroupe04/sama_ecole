using MediatR;

namespace SamaEcole.Application.Students.Commands.DeleteStudent;

/// <summary>
/// DELETE /api/v1/students/{id} — archive (soft delete) une fiche élève. Toujours un soft delete (AGENTS.md règle #6).
/// Réservé au Directeur et au Secrétariat.
///
/// Spec 2026-10-17 : le Handler refuse la suppression (409) tant qu'un paiement NON ANNULÉ est rattaché à l'élève
/// (FINANCIAL_RECORDS_EXIST, texte imposé) ou qu'un autre lien existe (DEPENDENCIES_EXIST, liste précise). Il ne rompt
/// JAMAIS un lien : c'est UnlinkStudentDependenciesCommand, demandée explicitement, qui s'en charge.
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateStudentCommand (règle #5).
/// </summary>
public record DeleteStudentCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
