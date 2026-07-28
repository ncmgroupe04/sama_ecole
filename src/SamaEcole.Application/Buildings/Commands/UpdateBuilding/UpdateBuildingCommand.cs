using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using MediatR;

namespace SamaEcole.Application.Buildings.Commands.UpdateBuilding;

/// <summary>
/// PUT /api/v1/buildings/{id} — corrige le nom/la description d'un bâtiment déjà créé.
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation : verrouillage optimiste
/// (AGENTS.md règle #5), même contrat que UpdateClassroomCommand.
/// </summary>
public record UpdateBuildingCommand(Guid Id, string Name, string? Description, uint RowVersion)
    : IRequest<BuildingResult>;
