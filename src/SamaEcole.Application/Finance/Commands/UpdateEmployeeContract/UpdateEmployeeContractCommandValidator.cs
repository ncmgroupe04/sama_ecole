using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;

public class UpdateEmployeeContractCommandValidator : AbstractValidator<UpdateEmployeeContractCommand>
{
    public UpdateEmployeeContractCommandValidator()
    {
        RuleFor(v => v.ContractId).NotEmpty();
        RuleFor(v => v.BaseSalary).GreaterThanOrEqualTo(0);
        RuleFor(v => v.HourlyRate).GreaterThanOrEqualTo(0);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0);

        // Même exigence que ChangeUserStatusCommand (JGK-A05) et UpdateClassFee : un motif vide ou
        // « ok » ne documente rien pour l'historique de rémunération.
        RuleFor(v => v.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}
