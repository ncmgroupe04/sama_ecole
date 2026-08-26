using MediatR;

namespace SamaEcole.Application.Inventory.Commands.CreateInventoryCategory;

/// <summary>
/// POST /api/v1/inventory/categories — module Inventaire. Le SchoolId n'est PAS ici : il est lu dans
/// le JWT via ITenantProvider (AGENTS.md règle #10), comme pour CreateBuildingCommand.
/// </summary>
public record CreateInventoryCategoryCommand : IRequest<InventoryCategoryResult>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public record InventoryCategoryResult(Guid Id, string Name, string? Description, uint RowVersion);
