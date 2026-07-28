using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.DeleteClassroom;

/// <summary>
/// DELETE /api/v1/classrooms/{id} — archive (soft delete) une classe créée par erreur. Toujours un
/// soft delete (AGENTS.md règle #6) : jamais de suppression physique.
///
/// Le Handler DOIT rejeter la suppression si des élèves sont encore rattachés à cette classe
/// (<see cref="Domain.Entities.Student.ClassroomId"/>) — voir DeleteClassroomCommandHandler. Une classe
/// vidée de ses élèves (transférés/réinscrits ailleurs) reste par contre archivable : l'historique
/// scolaire déjà écrit (inscriptions, bulletins) référence la classe par son Id, pas par un
/// verrou d'existence, et affiche « Classe supprimée » si elle disparaît de la liste active (voir
/// GetStudentDetailQueryHandler).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateClassroomCommand (règle #5).
/// </summary>
public record DeleteClassroomCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
