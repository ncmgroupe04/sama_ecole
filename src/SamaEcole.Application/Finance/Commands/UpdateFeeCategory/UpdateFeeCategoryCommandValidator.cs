using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.UpdateFeeCategory;

public class UpdateFeeCategoryCommandValidator : AbstractValidator<UpdateFeeCategoryCommand>
{
    public UpdateFeeCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
