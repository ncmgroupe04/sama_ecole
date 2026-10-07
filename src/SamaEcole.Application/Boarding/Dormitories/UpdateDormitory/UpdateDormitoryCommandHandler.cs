using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;

public class UpdateDormitoryCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateDormitoryCommand, DormitoryDto>
{
    public async Task<DormitoryDto> Handle(UpdateDormitoryCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà la recherche à l'école courante : viser un pavillon d'une
        // autre école renvoie 404, jamais une modification silencieuse.
        var dormitory = await dbContext.Dormitories
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Pavillon {request.Id} introuvable.");

        await DormitoryReader.EnsureValidSupervisorAsync(dbContext, dormitory.SchoolId, request.SupervisorUserId, cancellationToken);

        if (request.Gender != dormitory.Gender && await HasActiveBoardersAsync(dormitory.Id, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Gender),
                    "Ce pavillon héberge des pensionnaires : changez-les de pavillon avant de modifier son genre.")
            ]);
        }

        dbContext.SetOriginalConcurrencyToken(dormitory, request.RowVersion);

        dormitory.Name = request.Name.Trim();
        dormitory.Gender = request.Gender;
        dormitory.SupervisorName = request.SupervisorUserId is null ? request.SupervisorName?.Trim() : null;
        dormitory.SupervisorPhone = request.SupervisorPhone?.Trim();
        dormitory.SupervisorUserId = request.SupervisorUserId;
        dormitory.Notes = request.Notes?.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return await DormitoryReader.GetDtoAsync(dbContext, dormitory.Id, cancellationToken);
    }

    private async Task<bool> HasActiveBoardersAsync(Guid dormitoryId, CancellationToken cancellationToken)
    {
        var activeBeds = BoardingOccupancy.ActiveBedIds(dbContext);

        return await (from bed in dbContext.Beds
                      join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
                      where room.DormitoryId == dormitoryId && activeBeds.Contains(bed.Id)
                      select bed.Id)
            .AnyAsync(cancellationToken);
    }
}
