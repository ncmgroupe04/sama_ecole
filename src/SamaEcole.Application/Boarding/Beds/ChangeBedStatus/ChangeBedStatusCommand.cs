using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Beds.ChangeBedStatus;

/// <summary>
/// PUT /api/v1/boarding/beds/{id}/status — bascule un lit entre <c>Available</c> et <c>Maintenance</c>. <c>Occupied</c>
/// n'est jamais demandé : un lit devient occupé par une affectation (spec N2). Refusé en 409 RESOURCE_IN_USE pour un
/// lit occupé. <paramref name="RowVersion"/> : verrouillage optimiste (règle #5).
/// </summary>
public record ChangeBedStatusCommand(Guid Id, BedStatus Status, uint RowVersion) : IRequest<BedDto>;

public class ChangeBedStatusCommandValidator : AbstractValidator<ChangeBedStatusCommand>
{
    public ChangeBedStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Status).IsInEnum().WithMessage("Statut de lit invalide.")
            .NotEqual(BedStatus.Occupied)
            .WithMessage("Un lit devient occupé par une affectation, pas par un changement de statut.");
    }
}

public class ChangeBedStatusCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ChangeBedStatusCommand, BedDto>
{
    public async Task<BedDto> Handle(ChangeBedStatusCommand request, CancellationToken cancellationToken)
    {
        var bed = await dbContext.Beds
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Lit {request.Id} introuvable.");

        if (request.Status == BedStatus.Maintenance
            && await BoardingOccupancy.ActiveBedIds(dbContext).AnyAsync(id => id == bed.Id, cancellationToken))
        {
            throw new BusinessRuleException(
                "Impossible de mettre ce lit en maintenance : il est occupé.", SoftDeleteLifecycle.ResourceInUse);
        }

        dbContext.SetOriginalConcurrencyToken(bed, request.RowVersion);

        bed.Status = request.Status;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await BedReader.ListForRoomAsync(dbContext, bed.DormitoryRoomId, cancellationToken)).Single(b => b.Id == bed.Id);
    }
}
