using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.DeleteClassFee;

public class DeleteClassFeeCommandValidator : AbstractValidator<DeleteClassFeeCommand>
{
    public DeleteClassFeeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
