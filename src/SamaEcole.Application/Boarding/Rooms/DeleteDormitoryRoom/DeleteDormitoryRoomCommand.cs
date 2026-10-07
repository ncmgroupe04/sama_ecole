using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Rooms.DeleteDormitoryRoom;

/// <summary>
/// DELETE /api/v1/boarding/rooms/{id}?rowVersion= — soft delete (règle #6). Refusé en 409 RESOURCE_IN_USE tant que
/// la chambre possède des lits vivants : jamais de cascade (même règle que DeleteBuilding).
/// </summary>
public record DeleteDormitoryRoomCommand(Guid Id, uint RowVersion) : IRequest<Unit>;

public class DeleteDormitoryRoomCommandValidator : AbstractValidator<DeleteDormitoryRoomCommand>
{
    public DeleteDormitoryRoomCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public class DeleteDormitoryRoomCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteDormitoryRoomCommand, Unit>
{
    public async Task<Unit> Handle(DeleteDormitoryRoomCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var room = await dbContext.DormitoryRooms
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Chambre {request.Id} introuvable.");

        if (await dbContext.Beds.AnyAsync(b => b.DormitoryRoomId == request.Id, cancellationToken))
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : cette chambre possède des lits. Supprimez d'abord ses lits.",
                SoftDeleteLifecycle.ResourceInUse);
        }

        dbContext.SetOriginalConcurrencyToken(room, request.RowVersion);

        room.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
