using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Beds;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Dormitories.GetDormitory;

/// <summary>GET /api/v1/boarding/dormitories/{id} — un pavillon avec ses chambres et leurs lits (statut calculé).</summary>
public record GetDormitoryQuery(Guid Id) : IRequest<DormitoryDetailDto>;

public class GetDormitoryQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetDormitoryQuery, DormitoryDetailDto>
{
    public async Task<DormitoryDetailDto> Handle(GetDormitoryQuery request, CancellationToken cancellationToken)
    {
        // Pavillon d'une autre école ou supprimé : introuvable (filtre tenant + soft delete), jamais une fuite.
        var dormitory = await DormitoryReader.GetDtoAsync(dbContext, request.Id, cancellationToken);

        var rooms = await dbContext.DormitoryRooms.AsNoTracking()
            .Where(r => r.DormitoryId == request.Id)
            .OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, RowVersion = EF.Property<uint>(r, "xmin") })
            .ToListAsync(cancellationToken);

        // Une requête de lits par chambre : une chambre compte au plus quelques dizaines de lits et un pavillon une
        // dizaine de chambres, ce qui reste très en deçà d'un coût qui justifierait une jointure unique plus fragile.
        var roomDtos = new List<DormitoryRoomDto>(rooms.Count);
        foreach (var room in rooms)
        {
            var beds = await BedReader.ListForRoomAsync(dbContext, room.Id, cancellationToken);
            roomDtos.Add(new DormitoryRoomDto(room.Id, request.Id, room.Name, beds.Count, beds, room.RowVersion));
        }

        return new DormitoryDetailDto(
            dormitory.Id, dormitory.Name, dormitory.Gender, dormitory.SupervisorName, dormitory.SupervisorPhone,
            dormitory.SupervisorUserId, dormitory.Notes,
            roomDtos.Sum(r => r.Capacity),
            roomDtos.Sum(r => r.Beds.Count(b => b.Status == BedStatus.Occupied)),
            dormitory.RowVersion, roomDtos);
    }
}
