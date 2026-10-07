using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Rooms.RestoreDormitoryRoom;

/// <summary>POST /api/v1/boarding/rooms/{id}/restore — restaure une chambre supprimée logiquement.</summary>
public record RestoreDormitoryRoomCommand(Guid Id) : IRequest<Unit>;

public class RestoreDormitoryRoomCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreDormitoryRoomCommand, Unit>
{
    public async Task<Unit> Handle(RestoreDormitoryRoomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.DormitoryRooms, schoolId, request.Id, "Une chambre",
            r => other => other.DormitoryId == r.DormitoryId && other.Name == r.Name,
            // Une chambre ne revient jamais dans un pavillon supprimé : on restaure d'abord le pavillon.
            async (room, ct) =>
            {
                if (!await dbContext.Dormitories.AnyAsync(d => d.Id == room.DormitoryId, ct))
                {
                    throw new BusinessRuleException(
                        "Impossible de restaurer la chambre : son pavillon est supprimé. Restaurez d'abord le pavillon.",
                        "PARENT_ENTITY_ARCHIVED");
                }
            },
            cancellationToken);

        return Unit.Value;
    }
}
