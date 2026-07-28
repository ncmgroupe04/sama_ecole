using MediatR;

namespace SamaEcole.Application.Buildings.Commands.CreateBuilding;

/// <summary>
/// POST /api/v1/buildings — module Infrastructures.
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider, jamais accepté du client
/// (AGENTS.md règle #10).
/// </summary>
public record CreateBuildingCommand : IRequest<BuildingResult>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public record BuildingResult(Guid Id, string Name, string? Description, uint RowVersion);
