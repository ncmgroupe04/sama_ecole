using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Rooms.Commands.RestoreRoom;

/// <summary>POST /api/v1/rooms/{id}/restore — restaure une salle supprimée logiquement.</summary>
public record RestoreRoomCommand(Guid Id) : IRequest<Unit>;

public class RestoreRoomCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreRoomCommand, Unit>
{
    public async Task<Unit> Handle(RestoreRoomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.Rooms, schoolId, request.Id, "Une salle",
            r => other => other.BuildingId == r.BuildingId && other.Name == r.Name,
            // Une salle ne revient jamais dans un bâtiment supprimé : on restaure d'abord le bâtiment.
            async (room, ct) =>
            {
                if (!await dbContext.Buildings.AnyAsync(b => b.Id == room.BuildingId, ct))
                {
                    throw new BusinessRuleException(
                        "Impossible de restaurer la salle : son bâtiment est supprimé. Restaurez d'abord le bâtiment.",
                        "PARENT_ENTITY_ARCHIVED");
                }
            },
            cancellationToken);

        return Unit.Value;
    }
}
