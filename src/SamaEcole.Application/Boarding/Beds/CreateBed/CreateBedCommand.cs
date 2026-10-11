using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Beds.CreateBed;

/// <summary>
/// POST /api/v1/boarding/beds — ajoute un lit à une chambre existante. Sans <see cref="BedNumber"/>, le numéro est
/// le plus grand numéro de la chambre (lits vivants ET supprimés) + 1 : un numéro automatique ne retombe jamais sur
/// un lit archivé.
/// </summary>
public record CreateBedCommand : IRequest<BedDto>
{
    public Guid DormitoryRoomId { get; init; }
    public int? BedNumber { get; init; }
}

public class CreateBedCommandValidator : AbstractValidator<CreateBedCommand>
{
    public CreateBedCommandValidator()
    {
        RuleFor(x => x.DormitoryRoomId).NotEmpty();
        RuleFor(x => x.BedNumber).InclusiveBetween(1, 999).When(x => x.BedNumber is not null)
            .WithMessage("Le numéro de lit doit être compris entre 1 et 999.");
    }
}

public class CreateBedCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateBedCommand, BedDto>
{
    public async Task<BedDto> Handle(CreateBedCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.DormitoryRooms.AnyAsync(r => r.Id == request.DormitoryRoomId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.DormitoryRoomId), "La chambre indiquée n'existe pas dans votre établissement.")
            ]);
        }

        int number;
        if (request.BedNumber is { } explicitNumber)
        {
            number = explicitNumber;
            await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
                dbContext.Beds, schoolId, b => b.DormitoryRoomId == request.DormitoryRoomId && b.BedNumber == explicitNumber,
                $"Un lit n° {explicitNumber} dans cette chambre", cancellationToken);
        }
        else
        {
            // IgnoreQueryFilters lève AUSSI le filtre tenant : on réimpose SchoolId (la RLS reste la seconde barrière).
            var highest = await dbContext.Beds.IgnoreQueryFilters()
                .Where(b => b.SchoolId == schoolId && b.DormitoryRoomId == request.DormitoryRoomId)
                .Select(b => (int?)b.BedNumber)
                .MaxAsync(cancellationToken);
            number = (highest ?? 0) + 1;
        }

        var bed = new Bed { SchoolId = schoolId, DormitoryRoomId = request.DormitoryRoomId, BedNumber = number };
        dbContext.Beds.Add(bed);

        // Un numéro déjà pris viole l'index unique partiel → ConcurrencyConflictException (409).
        await dbContext.SaveChangesAsync(cancellationToken);

        return (await BedReader.ListForRoomAsync(dbContext, bed.DormitoryRoomId, cancellationToken)).Single(b => b.Id == bed.Id);
    }
}
