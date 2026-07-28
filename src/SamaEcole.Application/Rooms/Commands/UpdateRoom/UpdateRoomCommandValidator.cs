using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Rooms.Commands.UpdateRoom;

public class UpdateRoomCommandValidator : AbstractValidator<UpdateRoomCommand>
{
    public UpdateRoomCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("La capacité doit être supérieure à zéro.")
            .LessThanOrEqualTo(1000).WithMessage("La capacité annoncée semble irréaliste (maximum 1000).");
        RuleFor(x => x.Type).IsInEnum().WithMessage("Type de salle invalide.");
    }
}
