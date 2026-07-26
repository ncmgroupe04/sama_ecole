using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Buildings.Commands.DeleteBuilding;

public class DeleteBuildingCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<DeleteBuildingCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBuildingCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // un bâtiment d'une autre école renvoie 404, jamais une suppression silencieuse.
        var building = await dbContext.Buildings
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Bâtiment {request.Id} introuvable.");

        // Règle métier obligatoire : un bâtiment encore lié à des salles ne peut pas être archivé
        // (mêmes principes que DeleteClassroomCommandHandler vis-à-vis des élèves).
        var hasRooms = await dbContext.Rooms
            .AnyAsync(r => r.BuildingId == request.Id, cancellationToken);

        if (hasRooms)
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : ce bâtiment possède des salles rattachées.");
        }

        dbContext.SetOriginalConcurrencyToken(building, request.RowVersion);

        building.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
