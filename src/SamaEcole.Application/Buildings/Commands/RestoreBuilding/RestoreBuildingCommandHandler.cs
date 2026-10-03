using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

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

        // IgnoreQueryFilters lève le filtre tenant ET le filtre IsDeleted : le SchoolId est donc réimposé ici
        // (jamais fourni par le client). La RLS reste la seconde barrière. Un ID d'un autre établissement
        // renvoie 404, sans révéler son existence.
        var building = await dbContext.Buildings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == request.Id && b.SchoolId == schoolId && b.IsDeleted, cancellationToken)
            ?? throw new KeyNotFoundException($"Bâtiment supprimé {request.Id} introuvable.");

        var activeConflict = await dbContext.Buildings
            .AnyAsync(b => b.Name == building.Name, cancellationToken);
        if (activeConflict)
        {
            throw new BusinessRuleException(
                $"Impossible de restaurer : un bâtiment actif porte déjà le nom « {building.Name} ».",
                "ACTIVE_ENTITY_CONFLICT");
        }

        building.Restore();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
