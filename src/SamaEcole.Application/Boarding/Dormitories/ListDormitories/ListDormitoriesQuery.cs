using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Dormitories.ListDormitories;

/// <summary>
/// GET /api/v1/boarding/dormitories?gender= — pavillons de l'école avec capacité, occupation et taux d'occupation,
/// tous DÉRIVÉS des lits et des séjours actifs (spec N1/N2), jamais saisis ni stockés.
/// </summary>
public record ListDormitoriesQuery(DormitoryGender? Gender = null) : IRequest<IReadOnlyList<DormitorySummaryDto>>;

public class ListDormitoriesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListDormitoriesQuery, IReadOnlyList<DormitorySummaryDto>>
{
    public async Task<IReadOnlyList<DormitorySummaryDto>> Handle(
        ListDormitoriesQuery request, CancellationToken cancellationToken)
    {
        // Lecture pure. Le Global Query Filter + la RLS bornent déjà tout au tenant courant ET écartent les éléments
        // supprimés. Quatre requêtes d'agrégation simples assemblées en mémoire, plutôt qu'une jointure géante :
        // le nombre de pavillons d'une école se compte en dizaines au plus.
        var dormitories = await dbContext.Dormitories.AsNoTracking()
            .Where(d => request.Gender == null || d.Gender == request.Gender)
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id, d.Name, d.Gender, d.SupervisorName, d.SupervisorPhone, d.SupervisorUserId,
                LinkedName = dbContext.Users.Where(u => u.Id == d.SupervisorUserId).Select(u => u.FullName).FirstOrDefault(),
                RowVersion = EF.Property<uint>(d, "xmin")
            })
            .ToListAsync(cancellationToken);

        var roomCounts = (await dbContext.DormitoryRooms.AsNoTracking()
                .GroupBy(r => r.DormitoryId)
                .Select(g => new { DormitoryId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DormitoryId, x => x.Count);

        var bedCounts = (await (from b in dbContext.Beds.AsNoTracking()
                                join r in dbContext.DormitoryRooms on b.DormitoryRoomId equals r.Id
                                group b by r.DormitoryId into g
                                select new
                                {
                                    DormitoryId = g.Key,
                                    Capacity = g.Count(),
                                    Maintenance = g.Count(x => x.Status == BedStatus.Maintenance)
                                }).ToListAsync(cancellationToken))
            .ToDictionary(x => x.DormitoryId);

        // Lits occupés = lits tenus par un séjour actif, hors maintenance (la maintenance prime, jamais deux états).
        var occupiedCounts = (await (from be in dbContext.BoardingEnrollments.AsNoTracking()
                                     where be.IsActive && be.BedId != null
                                     join b in dbContext.Beds on be.BedId equals b.Id
                                     where b.Status != BedStatus.Maintenance
                                     join r in dbContext.DormitoryRooms on b.DormitoryRoomId equals r.Id
                                     group be by r.DormitoryId into g
                                     select new { DormitoryId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.DormitoryId, x => x.Count);

        return dormitories.Select(d =>
        {
            var capacity = bedCounts.TryGetValue(d.Id, out var beds) ? beds.Capacity : 0;
            var maintenance = beds?.Maintenance ?? 0;
            var occupied = occupiedCounts.GetValueOrDefault(d.Id);

            return new DormitorySummaryDto(
                d.Id, d.Name, d.Gender,
                d.SupervisorUserId is null ? d.SupervisorName : d.LinkedName,
                d.SupervisorPhone, d.SupervisorUserId,
                roomCounts.GetValueOrDefault(d.Id), capacity, occupied, maintenance,
                BoardingOccupancy.Rate(occupied, capacity, maintenance), d.RowVersion);
        }).ToList();
    }
}
