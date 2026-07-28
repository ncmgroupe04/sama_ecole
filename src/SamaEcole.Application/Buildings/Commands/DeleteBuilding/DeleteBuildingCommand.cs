using MediatR;

namespace SamaEcole.Application.Buildings.Commands.DeleteBuilding;

/// <summary>
/// DELETE /api/v1/buildings/{id} — archive (soft delete) un bâtiment créé par erreur. Toujours un
/// soft delete (AGENTS.md règle #6). Le Handler DOIT rejeter la suppression si des salles sont encore
/// rattachées à ce bâtiment (voir DeleteBuildingCommandHandler) — mêmes principes que
/// DeleteClassroomCommand vis-à-vis des élèves.
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateBuildingCommand (règle #5).
/// </summary>
public record DeleteBuildingCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
