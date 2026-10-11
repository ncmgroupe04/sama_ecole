using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Rooms.ListAvailableRooms;

/// <summary>
/// Une chambre d'internat qui peut encore accueillir quelqu'un, avec le pavillon qui la porte. <see cref="FreeBeds"/> compte les lits libres
/// (statut <c>Available</c> ET aucun séjour actif ne les tient), toujours au moins un.
/// </summary>
public record AvailableRoomDto(Guid RoomId, string RoomName, Guid DormitoryId, string DormitoryName, DormitoryGender Gender, int FreeBeds);

/// <summary>
/// GET /api/v1/boarding/available-rooms — chambres ayant au moins un lit libre. Remplace, pour le sélecteur de chambre du formulaire d'inscription,
/// l'ancien <c>GET /internat/dashboard</c> (lot F0). Lecture seule : le formulaire envoie ensuite une chambre à <c>POST /enrollments</c>, et c'est
/// <c>IBoardingAssignmentService</c> qui choisit le lit et juge (genre du pavillon, lit repris entre-temps) — cette liste n'est qu'un choix proposé.
/// </summary>
public record ListAvailableRoomsQuery : IRequest<IReadOnlyList<AvailableRoomDto>>;

public class ListAvailableRoomsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListAvailableRoomsQuery, IReadOnlyList<AvailableRoomDto>>
{
    public async Task<IReadOnlyList<AvailableRoomDto>> Handle(ListAvailableRoomsQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter et la RLS bornent au tenant courant et écartent les éléments supprimés : un lit, une chambre ou un pavillon supprimé
        // n'entre ni dans le décompte ni dans la liste (la jointure avec Dormitories exclut les chambres d'un pavillon supprimé).
        var heldBeds = dbContext.BoardingEnrollments.Where(be => be.IsActive && be.BedId != null).Select(be => be.BedId!.Value);

        var rooms = await (from bed in dbContext.Beds.AsNoTracking()
                           where bed.Status == BedStatus.Available && !heldBeds.Contains(bed.Id)
                           join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
                           join dormitory in dbContext.Dormitories on room.DormitoryId equals dormitory.Id
                           group bed by new { RoomId = room.Id, RoomName = room.Name, DormitoryId = dormitory.Id, DormitoryName = dormitory.Name, dormitory.Gender }
                           into g
                           orderby g.Key.DormitoryName, g.Key.RoomName
                           select new AvailableRoomDto(g.Key.RoomId, g.Key.RoomName, g.Key.DormitoryId, g.Key.DormitoryName, g.Key.Gender, g.Count()))
            .ToListAsync(cancellationToken);

        return rooms;
    }
}
