using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Rooms.CreateDormitoryRoom;

public class CreateDormitoryRoomCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateDormitoryRoomCommand, DormitoryRoomResult>
{
    public async Task<DormitoryRoomResult> Handle(CreateDormitoryRoomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter restreint déjà chaque requête au tenant courant : un pavillon d'une autre école y
        // est structurellement introuvable (même principe que CreateRoomCommandHandler pour le bâtiment).
        if (!await dbContext.Dormitories.AnyAsync(d => d.Id == request.DormitoryId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.DormitoryId), "Le pavillon indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var name = request.Name.Trim();
        await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
            dbContext.DormitoryRooms, schoolId, r => r.DormitoryId == request.DormitoryId && r.Name == name,
            $"Une chambre « {name} » dans ce pavillon", cancellationToken);

        var room = new DormitoryRoom { SchoolId = schoolId, DormitoryId = request.DormitoryId, Name = name };
        dbContext.DormitoryRooms.Add(room);

        for (var number = 1; number <= request.BedCount; number++)
        {
            dbContext.Beds.Add(new Bed { SchoolId = schoolId, DormitoryRoomId = room.Id, BedNumber = number });
        }

        // Une seule transaction : la chambre et tous ses lits, ou rien. Un doublon concurrent viole l'index
        // unique partiel → ConcurrencyConflictException (409), jamais un 500.
        await dbContext.SaveChangesAsync(cancellationToken);

        return await DormitoryRoomReader.GetResultAsync(dbContext, room.Id, cancellationToken);
    }
}
