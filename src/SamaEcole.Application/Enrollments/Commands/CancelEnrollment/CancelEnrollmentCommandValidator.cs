using FluentValidation;

namespace SamaEcole.Application.Enrollments.Commands.CancelEnrollment;

public class CancelEnrollmentCommandValidator : AbstractValidator<CancelEnrollmentCommand>
{
    public CancelEnrollmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
