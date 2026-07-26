using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Rooms.Commands.DeleteRoom;

public class DeleteRoomCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteRoomCommand, Unit>
{
    public async Task<Unit> Handle(DeleteRoomCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une salle d'une autre école renvoie 404, jamais une suppression silencieuse.
        var room = await dbContext.Rooms
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Salle {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(room, request.RowVersion);

        room.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
