using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Beds.RestoreBed;

/// <summary>POST /api/v1/boarding/beds/{id}/restore — restaure un lit supprimé logiquement.</summary>
public record RestoreBedCommand(Guid Id) : IRequest<Unit>;

public class RestoreBedCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreBedCommand, Unit>
{
    public async Task<Unit> Handle(RestoreBedCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.Beds, schoolId, request.Id, "Un lit",
            b => other => other.DormitoryRoomId == b.DormitoryRoomId && other.BedNumber == b.BedNumber,
            // Un lit ne revient jamais dans une chambre supprimée : on restaure d'abord la chambre.
            async (bed, ct) =>
            {
                if (!await dbContext.DormitoryRooms.AnyAsync(r => r.Id == bed.DormitoryRoomId, ct))
                {
                    throw new BusinessRuleException(
                        "Impossible de restaurer le lit : sa chambre est supprimée. Restaurez d'abord la chambre.",
                        "PARENT_ENTITY_ARCHIVED");
                }
            },
            cancellationToken);

        return Unit.Value;
    }
}
