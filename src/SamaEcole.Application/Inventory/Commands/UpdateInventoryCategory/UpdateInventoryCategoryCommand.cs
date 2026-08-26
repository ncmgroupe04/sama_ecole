using SamaEcole.Application.Inventory.Commands.CreateInventoryCategory;
using MediatR;

namespace SamaEcole.Application.Inventory.Commands.UpdateInventoryCategory;

/// <summary>PUT /api/v1/inventory/categories/{id}. RowVersion : verrou optimiste xmin (AGENTS.md règle #5).</summary>
public record UpdateInventoryCategoryCommand(Guid Id, string Name, string? Description, uint RowVersion)
    : IRequest<InventoryCategoryResult>;
