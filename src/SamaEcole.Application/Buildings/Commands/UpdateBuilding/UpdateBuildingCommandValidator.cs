using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Buildings.Commands.UpdateBuilding;

public class UpdateBuildingCommandValidator : AbstractValidator<UpdateBuildingCommand>
{
    public UpdateBuildingCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Description).MaximumLength(500).NoHtml();
    }
}
