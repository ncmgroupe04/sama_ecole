using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;

public class CreateFeeInstallmentPlanCommandValidator : AbstractValidator<CreateFeeInstallmentPlanCommand>
{
    public CreateFeeInstallmentPlanCommandValidator()
    {
        RuleFor(v => v.EnrollmentId).NotEmpty();
        RuleFor(v => v.Reason).MaximumLength(500).NoHtml();

        RuleFor(v => v.Installments)
            .NotEmpty().WithMessage("L'échéancier doit comporter au moins une échéance.")
            .Must(i => i.Count <= 24).WithMessage("Un échéancier ne peut pas dépasser 24 échéances.");

        RuleForEach(v => v.Installments).ChildRules(installment =>
        {
            installment.RuleFor(i => i.Label).NotEmpty().MaximumLength(120).NoHtml();
            installment.RuleFor(i => i.Amount).GreaterThan(0);
            installment.RuleFor(i => i.DueDate).NotEmpty();
        });
    }
}
