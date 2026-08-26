using MediatR;

namespace SamaEcole.Application.Inventory.Commands.DeleteInventoryItem;

/// <summary>DELETE /api/v1/inventory/items/{id} — archivage (soft delete), AGENTS.md règle #6.</summary>
public record DeleteInventoryItemCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
