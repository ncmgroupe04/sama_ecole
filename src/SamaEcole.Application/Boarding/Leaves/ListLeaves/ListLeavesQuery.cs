using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Leaves.ListLeaves;

/// <summary>
/// GET /api/v1/boarding/leaves — registre des sorties, plus récentes d'abord. <see cref="Status"/> applique en SQL le même
/// verdict que <see cref="BoardingLeaveStatus.Of(SamaEcole.Domain.Entities.BoardingLeave, DateOnly)"/>. <see cref="From"/> /
/// <see cref="To"/> retiennent les sorties dont l'intervalle [sortie, retour prévu] chevauche la période.
/// <see cref="UnlistedOnly"/> ne garde que les sorties avec un accompagnateur non habilité (la pastille du registre).
/// </summary>
public record ListLeavesQuery : IRequest<PaginatedLeaves>
{
    public Guid? BoarderId { get; init; }
    public Guid? DormitoryId { get; init; }
    public LeaveStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public bool UnlistedOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class ListLeavesQueryValidator : AbstractValidator<ListLeavesQuery>
{
    public ListLeavesQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status is not null)
            .WithMessage("Statut inconnu : choisissez Pending, Active, Overdue ou Returned.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("La fin de période ne peut pas précéder son début.");
    }
}

public class ListLeavesQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<ListLeavesQuery, PaginatedLeaves>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedLeaves> Handle(ListLeavesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var leaves = dbContext.BoardingLeaves.AsNoTracking();

        if (request.BoarderId is { } boarderId)
        {
            leaves = leaves.Where(l => l.BoardingEnrollmentId == boarderId);
        }

        if (request.DormitoryId is { } dormitoryId)
        {
            // Pavillon ACTUEL du pensionnaire : un séjour clos n'a plus de lit, ses sorties sortent de ce filtre.
            leaves = leaves.Where(l => (from be in dbContext.BoardingEnrollments
                                        join bed in dbContext.Beds on be.BedId equals bed.Id
                                        join r in dbContext.DormitoryRooms on bed.DormitoryRoomId equals r.Id
                                        where be.Id == l.BoardingEnrollmentId && r.DormitoryId == dormitoryId
                                        select be.Id).Any());
        }

        if (request.Status is { } status)
        {
            leaves = leaves.Where(BoardingLeaveStatus.Matches(status, today));
        }

        if (request.From is { } from)
        {
            leaves = leaves.Where(l => l.ExpectedReturnDate >= from);
        }

        if (request.To is { } to)
        {
            leaves = leaves.Where(l => l.LeaveDate <= to);
        }

        if (request.UnlistedOnly)
        {
            leaves = leaves.Where(l => l.IsCompanionUnlisted);
        }

        var total = await leaves.CountAsync(cancellationToken);

        var rows = await LeaveReader.Project(dbContext, leaves)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedLeaves(rows.Select(r => LeaveReader.ToDto(r, today)).ToList(), total, page, pageSize);
    }
}
