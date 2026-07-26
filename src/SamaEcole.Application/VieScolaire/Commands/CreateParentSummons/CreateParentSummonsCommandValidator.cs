using FluentValidation;

namespace SamaEcole.Application.VieScolaire.Commands.CreateParentSummons;

public class CreateParentSummonsCommandValidator : AbstractValidator<CreateParentSummonsCommand>
{
    public CreateParentSummonsCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.ScheduledAt).NotEmpty();
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(1000);
    }
}
