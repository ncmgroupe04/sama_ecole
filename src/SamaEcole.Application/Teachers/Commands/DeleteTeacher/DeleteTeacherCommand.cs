using MediatR;

namespace SamaEcole.Application.Teachers.Commands.DeleteTeacher;

/// <summary>
/// DELETE /api/v1/teachers/{id} — archive (soft delete) une fiche enseignant. Toujours un soft delete (AGENTS.md règle #6).
/// Réservé au Directeur et au Secrétariat.
///
/// Spec 2026-10-17 : le Handler refuse la suppression (409) tant qu'une FICHE DE PAIE est émise sur l'un de ses contrats
/// (FINANCIAL_RECORDS_EXIST) ou qu'un autre lien existe — contrat, affectations classe/matière, matières enseignées,
/// emploi du temps, pointages…, matériel prêté non restitué (DEPENDENCIES_EXIST, liste précise). Il ne rompt JAMAIS un
/// lien : c'est UnlinkTeacherDependenciesCommand, demandée explicitement, qui archive ceux qui peuvent l'être. Les
/// qualifications (TeacherSubject), autrefois silencieuses, sont désormais listées et rompues explicitement.
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateTeacherCommand (règle #5).
/// </summary>
public record DeleteTeacherCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
