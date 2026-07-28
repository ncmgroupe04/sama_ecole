using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.UpdateClassFee;

public class UpdateClassFeeCommandValidator : AbstractValidator<UpdateClassFeeCommand>
{
    public UpdateClassFeeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Le montant ne peut pas être négatif.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Le montant annoncé semble irréaliste.");
    }
}
