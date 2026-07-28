using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CloseEmployeeContract;

public class CloseEmployeeContractCommandValidator : AbstractValidator<CloseEmployeeContractCommand>
{
    public CloseEmployeeContractCommandValidator()
    {
        RuleFor(v => v.ContractId).NotEmpty();
        RuleFor(v => v.EndDate).NotEmpty();

        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}
