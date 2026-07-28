using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.DeleteFeeCategory;

public class DeleteFeeCategoryCommandValidator : AbstractValidator<DeleteFeeCategoryCommand>
{
    public DeleteFeeCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
