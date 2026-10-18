using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Rooms.Deletion;
using MediatR;

namespace SamaEcole.Application.Rooms.Commands.UnlinkRoomDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'une salle (spec 2026-10-17, §2.1). Ne supprime PAS l'élément : c'est
/// DeleteRoomCommand, qui ne rompt jamais rien. Chaque lien est archivé ou détaché (rien n'est effacé).
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkRoomDependenciesCommand(Guid RoomId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkRoomDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkRoomDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkRoomDependenciesCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, RoomDeletionPolicy.Policy, request.RoomId, actorId.ToString(), ct),
            cancellationToken);
    }
}
