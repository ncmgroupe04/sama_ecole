using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;

public class CreateFinancialCommitmentCommandValidator : AbstractValidator<CreateFinancialCommitmentCommand>
{
    public CreateFinancialCommitmentCommandValidator()
    {
        RuleFor(v => v.EnrollmentId).NotEmpty();
        RuleFor(v => v.Amount).GreaterThan(0);
        RuleFor(v => v.DueDate).NotEmpty();
        RuleFor(v => v.Terms).NotEmpty().MaximumLength(2000);
    }
}
