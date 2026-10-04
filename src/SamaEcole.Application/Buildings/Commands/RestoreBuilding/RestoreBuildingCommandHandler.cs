using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Buildings.Commands.RestoreBuilding;

public class RestoreBuildingCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<RestoreBuildingCommand, Unit>
{
    public async Task<Unit> Handle(RestoreBuildingCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // 404 hors tenant, 409 ACTIVE_ENTITY_CONFLICT si le nom est repris : voir SoftDeleteLifecycle.
        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.Buildings, schoolId, request.Id, "Un bâtiment",
            b => other => other.Name == b.Name, beforeRestore: null, cancellationToken);

        return Unit.Value;
    }
}
