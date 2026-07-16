using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.UpdateGrade;

public class UpdateGradeCommandValidator : AbstractValidator<UpdateGradeCommand>
{
    public UpdateGradeCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.Value)
            .GreaterThanOrEqualTo(0).WithMessage("Une note ne peut pas être négative.");
    }
}
