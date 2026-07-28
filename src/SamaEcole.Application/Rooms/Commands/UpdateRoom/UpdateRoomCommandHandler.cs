using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Rooms.Commands.CreateRoom;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Rooms.Commands.UpdateRoom;

public class UpdateRoomCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateRoomCommand, RoomResult>
{
    public async Task<RoomResult> Handle(UpdateRoomCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une salle d'une autre école renvoie 404, jamais une modification silencieuse.
        var room = await dbContext.Rooms
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Salle {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(room, request.RowVersion);

        room.Name = request.Name.Trim();
        room.Capacity = request.Capacity;
        room.Type = request.Type;

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Rooms.AsNoTracking()
            .Where(r => r.Id == room.Id)
            .Select(r => EF.Property<uint>(r, "xmin"))
            .FirstAsync(cancellationToken);

        return new RoomResult(room.Id, room.Name, room.Capacity, room.Type, room.BuildingId, newRowVersion);
    }
}
