using FluentValidation;

namespace SamaEcole.Application.Rooms.Commands.DeleteRoom;

public class DeleteRoomCommandValidator : AbstractValidator<DeleteRoomCommand>
{
    public DeleteRoomCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
