using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.OpenCashierSession;

public class OpenCashierSessionCommandValidator : AbstractValidator<OpenCashierSessionCommand>
{
    public OpenCashierSessionCommandValidator()
    {
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0);
    }
}
