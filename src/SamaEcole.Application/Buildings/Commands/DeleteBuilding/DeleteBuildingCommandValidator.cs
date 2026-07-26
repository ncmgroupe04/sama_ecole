using FluentValidation;

namespace SamaEcole.Application.Buildings.Commands.DeleteBuilding;

public class DeleteBuildingCommandValidator : AbstractValidator<DeleteBuildingCommand>
{
    public DeleteBuildingCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
