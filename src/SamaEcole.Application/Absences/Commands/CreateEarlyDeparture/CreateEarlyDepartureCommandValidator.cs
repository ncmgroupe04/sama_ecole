using FluentValidation;

namespace SamaEcole.Application.Absences.Commands.CreateEarlyDeparture;

public class CreateEarlyDepartureCommandValidator : AbstractValidator<CreateEarlyDepartureCommand>
{
    public CreateEarlyDepartureCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(v => v.PickedUpBy).MaximumLength(200);
    }
}
