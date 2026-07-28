using FluentValidation;

namespace SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;

public class CreateAbsenceJustificationCommandValidator : AbstractValidator<CreateAbsenceJustificationCommand>
{
    public CreateAbsenceJustificationCommandValidator()
    {
        RuleFor(v => v.StudentId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Reason).NotEmpty().MaximumLength(1000);
    }
}
