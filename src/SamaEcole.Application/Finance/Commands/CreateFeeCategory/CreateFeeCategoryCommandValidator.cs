using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

public class CreateFeeCategoryCommandValidator : AbstractValidator<CreateFeeCategoryCommand>
{
    public CreateFeeCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60);
    }
}
