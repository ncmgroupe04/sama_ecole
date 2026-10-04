using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Buildings.Commands.CreateBuilding;

public class CreateBuildingCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateBuildingCommand, BuildingResult>
{
    public async Task<BuildingResult> Handle(CreateBuildingCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var name = request.Name.Trim();

        // Jamais de réactivation silencieuse : une identité présente seulement parmi les supprimés est refusée
        // en 409 ARCHIVED_ENTITY_EXISTS (conception soft delete §3.2) ; l'index unique partiel arbitre le reste.
        await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
            dbContext.Buildings, schoolId, b => b.Name == name, $"Un bâtiment « {name} »", cancellationToken);

        var building = new Building
        {
            SchoolId = schoolId,
            Name = name,
            Description = request.Description?.Trim()
        };

        dbContext.Buildings.Add(building);

        // Deux bâtiments de même nom dans la même école violent l'index unique : SaveChangesAsync
        // traduit la violation en ConcurrencyConflictException → 409, jamais un écrasement silencieux
        // ni un 500 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.Buildings.AsNoTracking()
            .Where(b => b.Id == building.Id)
            .Select(b => EF.Property<uint>(b, "xmin"))
            .FirstAsync(cancellationToken);

        return new BuildingResult(building.Id, building.Name, building.Description, rowVersion);
    }
}
