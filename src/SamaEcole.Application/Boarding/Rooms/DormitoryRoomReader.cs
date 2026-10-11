using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Beds;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Rooms;

/// <summary>Chambre relue APRÈS écriture, avec ses lits (statut calculé) et son jeton xmin réel.</summary>
internal static class DormitoryRoomReader
{
    public static async Task<DormitoryRoomResult> GetResultAsync(
        IApplicationDbContext dbContext, Guid roomId, CancellationToken cancellationToken)
    {
        var room = await dbContext.DormitoryRooms.AsNoTracking()
            .Where(r => r.Id == roomId)
            .Select(r => new { r.Id, r.DormitoryId, r.Name, RowVersion = EF.Property<uint>(r, "xmin") })
            .FirstAsync(cancellationToken);

        var beds = await BedReader.ListForRoomAsync(dbContext, roomId, cancellationToken);

        return new DormitoryRoomResult(room.Id, room.DormitoryId, room.Name, beds, room.RowVersion);
    }
}
