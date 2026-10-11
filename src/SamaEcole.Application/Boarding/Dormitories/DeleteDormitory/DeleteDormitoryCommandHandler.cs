using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Dormitories.DeleteDormitory;

public class DeleteDormitoryCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteDormitoryCommand, Unit>
{
    public async Task<Unit> Handle(DeleteDormitoryCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var dormitory = await dbContext.Dormitories
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pavillon {request.Id} introuvable.");

        // Même règle que DeleteBuilding : jamais de cascade, on supprime d'abord les chambres.
        if (await dbContext.DormitoryRooms.AnyAsync(r => r.DormitoryId == request.Id, cancellationToken))
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : ce pavillon possède des chambres rattachées. Supprimez d'abord ses chambres.",
                SoftDeleteLifecycle.ResourceInUse);
        }

        dbContext.SetOriginalConcurrencyToken(dormitory, request.RowVersion);

        dormitory.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
