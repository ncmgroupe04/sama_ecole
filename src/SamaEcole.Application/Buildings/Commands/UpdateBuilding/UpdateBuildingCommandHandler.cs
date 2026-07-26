using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Buildings.Commands.UpdateBuilding;

public class UpdateBuildingCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateBuildingCommand, BuildingResult>
{
    public async Task<BuildingResult> Handle(UpdateBuildingCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // un bâtiment d'une autre école renvoie 404, jamais une modification silencieuse.
        var building = await dbContext.Buildings
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bâtiment {request.Id} introuvable.");

        // Cœur du verrou optimiste (AGENTS.md règle #5) : le jeton lu par le client devient la valeur
        // d'origine imposée à EF.
        dbContext.SetOriginalConcurrencyToken(building, request.RowVersion);

        building.Name = request.Name.Trim();
        building.Description = request.Description?.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Buildings.AsNoTracking()
            .Where(b => b.Id == building.Id)
            .Select(b => EF.Property<uint>(b, "xmin"))
            .FirstAsync(cancellationToken);

        return new BuildingResult(building.Id, building.Name, building.Description, newRowVersion);
    }
}
