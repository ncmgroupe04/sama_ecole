using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryItems;

public class GetInventoryItemsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetInventoryItemsQuery, PaginatedInventoryItems>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedInventoryItems> Handle(
        GetInventoryItemsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        // Aucun filtre sur SchoolId ici, volontairement : le Global Query Filter l'applique
        // automatiquement, et la policy RLS PostgreSQL le rejouerait même si ce filtre disparaissait
        // un jour (AGENTS.md règle #2).
        var query = dbContext.InventoryItems.AsNoTracking();

        if (request.CategoryId is { } categoryId)
        {
            query = query.Where(i => i.CategoryId == categoryId);
        }

        if (request.RoomId is { } roomId)
        {
            query = query.Where(i => i.RoomId == roomId);
        }

        if (request.Condition is { } condition)
        {
            query = query.Where(i => i.Condition == condition);
        }

        if (request.OutOfStockOnly)
        {
            query = query.Where(i => i.QuantityAvailable == 0);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Volontairement écrit avec ToLower() et non avec EF.Functions.ILike, qui serait plus
            // direct : ILike est une extension Npgsql, et SamaEcole.Application ne doit connaître
            // aucun provider (AGENTS.md règle #1) — même arbitrage que GetStudentsQueryHandler.
            // Npgsql traduit ToLower() en lower(), le comportement est donc le même.
            var search = request.Search.Trim().ToLower();

            query = query.Where(i =>
                i.Name.ToLower().Contains(search)
                || (i.Code != null && i.Code.ToLower().Contains(search)));
        }

        var totalCount = await CountOrZeroAsync(query, cancellationToken);

        var items = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query
                .OrderBy(i => i.Name)
                .ThenBy(i => i.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new InventoryItemListItem(
                    i.Id,
                    i.Name,
                    i.Code,
                    i.CategoryId,
                    dbContext.InventoryCategories
                        .Where(c => c.Id == i.CategoryId)
                        .Select(c => c.Name)
                        .FirstOrDefault() ?? "Catégorie supprimée",
                    i.QuantityTotal,
                    i.QuantityAvailable,
                    i.QuantityTotal - i.QuantityAvailable,
                    i.Condition.ToString(),
                    i.RoomId,
                    i.RoomId == null
                        ? null
                        : dbContext.Rooms.Where(r => r.Id == i.RoomId).Select(r => r.Name).FirstOrDefault(),
                    i.LocationLabel,
                    i.UnitPrice,
                    i.IsConsumable,
                    EF.Property<uint>(i, "xmin"))),
            cancellationToken);

        return new PaginatedInventoryItems(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Décompte total de la page, avec la même tolérance à une migration non appliquée que
    /// ToListOrEmptyOnMissingTableAsync — qui ne sait renvoyer qu'une LISTE. Le GroupBy sur une
    /// constante fait produire à PostgreSQL un unique COUNT(*) : c'est bien une ligne qui remonte,
    /// pas le catalogue entier ramené en mémoire pour être compté.
    /// </summary>
    private async Task<int> CountOrZeroAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
    {
        var counted = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query.GroupBy(_ => 1).Select(group => group.Count()), cancellationToken);

        return counted.FirstOrDefault();
    }
}
