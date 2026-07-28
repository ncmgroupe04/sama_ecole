using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;

public class GetBuildingsWithRoomsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetBuildingsWithRoomsQuery, IReadOnlyList<BuildingWithRoomsDto>>
{
    public async Task<IReadOnlyList<BuildingWithRoomsDto>> Handle(
        GetBuildingsWithRoomsQuery request, CancellationToken cancellationToken)
    {
        // AsNoTracking : lecture pure. Le Global Query Filter + la RLS bornent déjà bâtiments ET
        // salles au tenant courant — la sous-collection Rooms n'a besoin d'aucun filtre manuel.
        //
        // ToListOrEmptyOnMissingTableAsync (et non ToListAsync) : module récent, dont la migration
        // peut ne pas encore être appliquée sur certains environnements — l'écran d'accueil du module
        // doit alors afficher son état vide normal, jamais un 500 brut.
        var buildings = await dbContext.ToListOrEmptyOnMissingTableAsync(
            dbContext.Buildings
                .AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Description,
                    RowVersion = EF.Property<uint>(b, "xmin")
                }),
            cancellationToken);

        var rooms = await dbContext.ToListOrEmptyOnMissingTableAsync(
            dbContext.Rooms
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.Capacity,
                    Type = r.Type.ToString(),
                    r.BuildingId,
                    RowVersion = EF.Property<uint>(r, "xmin")
                }),
            cancellationToken);

        var roomsByBuilding = rooms.ToLookup(r => r.BuildingId);

        return buildings
            .Select(b => new BuildingWithRoomsDto(
                b.Id,
                b.Name,
                b.Description,
                b.RowVersion,
                roomsByBuilding[b.Id]
                    .Select(r => new RoomDto(r.Id, r.Name, r.Capacity, r.Type, r.BuildingId, r.RowVersion))
                    .ToList()))
            .ToList();
    }
}
