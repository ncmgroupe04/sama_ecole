using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetItemAssignments;

public class GetItemAssignmentsQueryHandler(
    IApplicationDbContext dbContext,
    TimeProvider timeProvider)
    : IRequestHandler<GetItemAssignmentsQuery, PaginatedItemAssignments>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedItemAssignments> Handle(
        GetItemAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var query = dbContext.ItemAssignments.AsNoTracking();

        if (request.ItemId is { } itemId)
        {
            query = query.Where(a => a.ItemId == itemId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (request.StudentId is { } studentId)
        {
            query = query.Where(a => a.StudentId == studentId);
        }

        if (request.TeacherId is { } teacherId)
        {
            query = query.Where(a => a.TeacherId == teacherId);
        }

        if (request.OverdueOnly)
        {
            // « En retard » n'a de sens que pour un prêt encore ouvert ET doté d'une échéance : une
            // fiche restituée n'est jamais en retard, et un prêt sans date de retour prévue non plus.
            query = query.Where(a =>
                (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue)
                && a.DueOn != null
                && a.DueOn < today);
        }

        var totalCount = (await dbContext.ToListOrEmptyOnMissingTableAsync(
            query.GroupBy(_ => 1).Select(group => group.Count()), cancellationToken)).FirstOrDefault();

        var rows = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query
                .OrderByDescending(a => a.AssignedOn)
                .ThenBy(a => a.BeneficiaryLabel)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.Id,
                    a.ItemId,
                    ItemName = dbContext.InventoryItems
                        .Where(i => i.Id == a.ItemId)
                        .Select(i => i.Name)
                        .FirstOrDefault() ?? "Bien archivé",
                    ItemCode = dbContext.InventoryItems
                        .Where(i => i.Id == a.ItemId)
                        .Select(i => i.Code)
                        .FirstOrDefault(),
                    a.Quantity,
                    a.ReturnedQuantity,
                    BeneficiaryType = a.BeneficiaryType.ToString(),
                    BeneficiaryId = a.StudentId ?? a.TeacherId ?? a.UserId,
                    a.BeneficiaryLabel,
                    a.AssignedOn,
                    a.DueOn,
                    a.ReturnedOn,
                    ReturnCondition = a.ReturnCondition == null ? null : a.ReturnCondition.ToString(),
                    Status = a.Status.ToString(),
                    IsStillOpen = a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue,
                    RowVersion = EF.Property<uint>(a, "xmin")
                }),
            cancellationToken);

        var items = rows
            .Select(a => new ItemAssignmentListItem(
                a.Id,
                AssignmentReference.For(a.Id, a.AssignedOn),
                a.ItemId,
                a.ItemName,
                a.ItemCode,
                a.Quantity,
                a.ReturnedQuantity ?? 0,
                a.BeneficiaryType,
                // La contrainte CHECK garantit qu'exactement une des trois clés est renseignée : ce
                // GetValueOrDefault ne masque donc aucun cas réel, il satisfait le compilateur.
                a.BeneficiaryId.GetValueOrDefault(),
                a.BeneficiaryLabel,
                a.AssignedOn,
                a.DueOn,
                a.ReturnedOn,
                a.ReturnCondition,
                a.IsStillOpen && a.DueOn.HasValue && a.DueOn.Value < today,
                a.Status,
                a.RowVersion))
            .ToList();

        return new PaginatedItemAssignments(items, totalCount, page, pageSize);
    }
}
