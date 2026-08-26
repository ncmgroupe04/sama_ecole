using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetInventoryItemDetail;

public class GetInventoryItemDetailQueryHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<GetInventoryItemDetailQuery, InventoryItemDetailDto>
{
    /// <summary>Assez pour raconter la vie récente du lot sans transformer la fiche en journal complet.</summary>
    private const int RecentMovementCount = 20;

    public async Task<InventoryItemDetailDto> Handle(
        GetInventoryItemDetailQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : le
        // bien d'une autre école renvoie 404, jamais une fiche remplie.
        var item = await dbContext.InventoryItems.AsNoTracking()
            .Where(i => i.Id == request.Id)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Code,
                i.CategoryId,
                CategoryName = dbContext.InventoryCategories
                    .Where(c => c.Id == i.CategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "Catégorie supprimée",
                i.QuantityTotal,
                i.QuantityAvailable,
                Condition = i.Condition.ToString(),
                i.RoomId,
                RoomName = i.RoomId == null
                    ? null
                    : dbContext.Rooms.Where(r => r.Id == i.RoomId).Select(r => r.Name).FirstOrDefault(),
                i.LocationLabel,
                i.UnitPrice,
                i.IsConsumable,
                i.Notes,
                RowVersion = EF.Property<uint>(i, "xmin")
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {request.Id} introuvable.");

        var movements = await dbContext.StockMovements.AsNoTracking()
            .Where(m => m.ItemId == request.Id)
            .OrderByDescending(m => m.MovementDate)
            .ThenByDescending(m => m.CreatedAt)
            .Take(RecentMovementCount)
            .Select(m => new InventoryItemMovementDto(
                m.Id,
                m.Type.ToString(),
                m.Quantity,
                m.MovementDate,
                m.Reason,
                m.CounterpartyLabel,
                m.QuantityTotalAfter,
                m.QuantityAvailableAfter))
            .ToListAsync(cancellationToken);

        var openAssignments = await dbContext.ItemAssignments.AsNoTracking()
            .Where(a => a.ItemId == request.Id
                        && (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue))
            .OrderBy(a => a.DueOn ?? DateOnly.MaxValue)
            .ThenBy(a => a.BeneficiaryLabel)
            .Select(a => new
            {
                a.Id,
                a.AssignedOn,
                BeneficiaryType = a.BeneficiaryType.ToString(),
                a.BeneficiaryLabel,
                a.Quantity,
                a.ReturnedQuantity,
                a.DueOn,
                Status = a.Status.ToString(),
                RowVersion = EF.Property<uint>(a, "xmin")
            })
            .ToListAsync(cancellationToken);

        return new InventoryItemDetailDto(
            item.Id,
            item.Name,
            item.Code,
            item.CategoryId,
            item.CategoryName,
            item.QuantityTotal,
            item.QuantityAvailable,
            item.QuantityTotal - item.QuantityAvailable,
            item.Condition,
            item.RoomId,
            item.RoomName,
            item.LocationLabel,
            item.UnitPrice,
            item.IsConsumable,
            item.Notes,
            item.RowVersion,
            movements,
            openAssignments
                .Select(a => new InventoryItemOpenAssignmentDto(
                    a.Id,
                    AssignmentReference.For(a.Id, a.AssignedOn),
                    a.BeneficiaryType,
                    a.BeneficiaryLabel,
                    a.Quantity,
                    a.ReturnedQuantity ?? 0,
                    a.AssignedOn,
                    a.DueOn,
                    a.DueOn.HasValue && a.DueOn.Value < today,
                    a.Status,
                    a.RowVersion))
                .ToList());
    }
}
