using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Buildings.Commands.CreateBuilding;

public class CreateBuildingCommandValidator : AbstractValidator<CreateBuildingCommand>
{
    public CreateBuildingCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.Description).MaximumLength(500).NoHtml();
    }
}
