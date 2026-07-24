using FluentValidation;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

public class CreateLateArrivalCommandValidator : AbstractValidator<CreateLateArrivalCommand>
{
    public CreateLateArrivalCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Minutes).GreaterThan(0);
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(1000);
    }
}
