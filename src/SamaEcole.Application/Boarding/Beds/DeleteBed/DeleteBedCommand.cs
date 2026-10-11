using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Beds.DeleteBed;

/// <summary>
/// DELETE /api/v1/boarding/beds/{id}?rowVersion= — soft delete (règle #6). Refusé en 409 RESOURCE_IN_USE pour un lit
/// occupé par un séjour actif.
/// </summary>
public record DeleteBedCommand(Guid Id, uint RowVersion) : IRequest<Unit>;

public class DeleteBedCommandValidator : AbstractValidator<DeleteBedCommand>
{
    public DeleteBedCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public class DeleteBedCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteBedCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBedCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var bed = await dbContext.Beds
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Lit {request.Id} introuvable.");

        if (await BoardingOccupancy.ActiveBedIds(dbContext).AnyAsync(id => id == bed.Id, cancellationToken))
        {
            throw new BusinessRuleException(
                "Impossible de supprimer : ce lit est occupé.", SoftDeleteLifecycle.ResourceInUse);
        }

        dbContext.SetOriginalConcurrencyToken(bed, request.RowVersion);

        bed.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
