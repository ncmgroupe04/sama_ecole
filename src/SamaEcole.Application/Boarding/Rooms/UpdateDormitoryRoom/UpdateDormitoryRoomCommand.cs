using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Boarding.Rooms.UpdateDormitoryRoom;

/// <summary>
/// PUT /api/v1/boarding/rooms/{id} — renomme une chambre. Ne déplace pas la chambre vers un autre pavillon et ne
/// touche pas à ses lits (ils se gèrent par /beds). <paramref name="RowVersion"/> : verrouillage optimiste (règle #5).
/// </summary>
public record UpdateDormitoryRoomCommand(Guid Id, string Name, uint RowVersion) : IRequest<DormitoryRoomResult>;

public class UpdateDormitoryRoomCommandValidator : AbstractValidator<UpdateDormitoryRoomCommand>
{
    public UpdateDormitoryRoomCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
    }
}

public class UpdateDormitoryRoomCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateDormitoryRoomCommand, DormitoryRoomResult>
{
    public async Task<DormitoryRoomResult> Handle(UpdateDormitoryRoomCommand request, CancellationToken cancellationToken)
    {
        var room = await dbContext.DormitoryRooms
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Chambre {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(room, request.RowVersion);

        room.Name = request.Name.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return await DormitoryRoomReader.GetResultAsync(dbContext, room.Id, cancellationToken);
    }
}
