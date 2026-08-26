using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetStockMovements;

public class GetStockMovementsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStockMovementsQuery, PaginatedStockMovements>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedStockMovements> Handle(
        GetStockMovementsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var query = dbContext.StockMovements.AsNoTracking();

        if (request.ItemId is { } itemId)
        {
            query = query.Where(m => m.ItemId == itemId);
        }

        if (request.Type is { } type)
        {
            query = query.Where(m => m.Type == type);
        }

        if (request.From is { } from)
        {
            query = query.Where(m => m.MovementDate >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(m => m.MovementDate <= to);
        }

        var totalCount = (await dbContext.ToListOrEmptyOnMissingTableAsync(
            query.GroupBy(_ => 1).Select(group => group.Count()), cancellationToken)).FirstOrDefault();

        var items = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query
                // Le journal se lit du plus récent au plus ancien ; CreatedAt départage deux mouvements
                // du même jour, que MovementDate seule laisserait dans un ordre indéterminé.
                .OrderByDescending(m => m.MovementDate)
                .ThenByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new StockMovementListItem(
                    m.Id,
                    m.ItemId,
                    dbContext.InventoryItems
                        .Where(i => i.Id == m.ItemId)
                        .Select(i => i.Name)
                        .FirstOrDefault() ?? "Bien archivé",
                    dbContext.InventoryItems
                        .Where(i => i.Id == m.ItemId)
                        .Select(i => i.Code)
                        .FirstOrDefault(),
                    m.Type.ToString(),
                    m.Quantity,
                    m.MovementDate,
                    m.Reason,
                    m.CounterpartyLabel,
                    m.AssignmentId,
                    m.QuantityTotalAfter,
                    m.QuantityAvailableAfter)),
            cancellationToken);

        return new PaginatedStockMovements(items, totalCount, page, pageSize);
    }
}
