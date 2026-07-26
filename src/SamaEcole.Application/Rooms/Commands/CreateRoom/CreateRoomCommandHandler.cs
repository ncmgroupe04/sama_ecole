using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Rooms.Commands.CreateRoom;

public class CreateRoomCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateRoomCommand, RoomResult>
{
    public async Task<RoomResult> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le bâtiment doit exister DANS CETTE ÉCOLE. Le Global Query Filter restreint déjà chaque
        // requête au tenant courant : un bâtiment d'une autre école y est structurellement introuvable
        // (même principe que CreateGradeCommandHandler vis-à-vis de l'élève/la matière/le trimestre).
        if (!await dbContext.Buildings.AnyAsync(b => b.Id == request.BuildingId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.BuildingId), "Le bâtiment indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var room = new Room
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            Capacity = request.Capacity,
            Type = request.Type,
            BuildingId = request.BuildingId
        };

        dbContext.Rooms.Add(room);

        // Deux salles de même nom dans le même bâtiment violent l'index unique : SaveChangesAsync
        // traduit la violation en ConcurrencyConflictException → 409, jamais un écrasement silencieux
        // ni un 500 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.Rooms.AsNoTracking()
            .Where(r => r.Id == room.Id)
            .Select(r => EF.Property<uint>(r, "xmin"))
            .FirstAsync(cancellationToken);

        return new RoomResult(room.Id, room.Name, room.Capacity, room.Type, room.BuildingId, rowVersion);
    }
}
