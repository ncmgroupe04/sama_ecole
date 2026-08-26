using MediatR;

namespace SamaEcole.Application.Inventory.Commands.DeleteInventoryCategory;

/// <summary>DELETE /api/v1/inventory/categories/{id} — archivage (soft delete), AGENTS.md règle #6.</summary>
public record DeleteInventoryCategoryCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
