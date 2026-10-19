using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Buildings.Deletion;
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

        // Spec 2026-10-17 : la suppression NE ROMPT JAMAIS un lien. Une donnée financière active la refuse (texte
        // imposé) ; tout autre lien restant la refuse en les énumérant — l'utilisateur les rompt avant, explicitement
        // (UnlinkBuildingDependenciesCommand).
        await EntityDeletionAnalyzer.EnsureDeletableAsync(
            dbContext, BuildingDeletionPolicy.Policy, request.Id, cancellationToken);

        dbContext.SetOriginalConcurrencyToken(building, request.RowVersion);

        building.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
