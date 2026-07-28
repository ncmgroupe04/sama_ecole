using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.DeleteGrade;

public class DeleteGradeCommandValidator : AbstractValidator<DeleteGradeCommand>
{
    public DeleteGradeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
