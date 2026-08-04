using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CloseCashierSession;

public class CloseCashierSessionCommandValidator : AbstractValidator<CloseCashierSessionCommand>
{
    public CloseCashierSessionCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
