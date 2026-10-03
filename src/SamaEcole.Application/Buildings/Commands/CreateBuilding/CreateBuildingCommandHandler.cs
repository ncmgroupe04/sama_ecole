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

        // Identité absente des bâtiments actifs mais présente parmi les supprimés : jamais de réactivation
        // silencieuse, l'utilisateur est invité à restaurer (conception soft delete §3.2). Si une ligne active
        // porte déjà le nom, c'est l'index unique partiel qui arbitre (conflit de doublon existant).
        var activeExists = await dbContext.Buildings.AnyAsync(b => b.Name == name, cancellationToken);
        if (!activeExists)
        {
            var archivedExists = await dbContext.Buildings.IgnoreQueryFilters()
                .AnyAsync(b => b.SchoolId == schoolId && b.IsDeleted && b.Name == name, cancellationToken);
            if (archivedExists)
            {
                throw new BusinessRuleException(
                    $"Un bâtiment « {name} » existe dans les éléments supprimés : restaurez-le au lieu d'en créer un nouveau.",
                    "ARCHIVED_ENTITY_EXISTS");
            }
        }

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
