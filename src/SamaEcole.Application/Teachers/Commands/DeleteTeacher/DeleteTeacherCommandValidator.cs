using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.DeleteTeacher;

public class DeleteTeacherCommandValidator : AbstractValidator<DeleteTeacherCommand>
{
    public DeleteTeacherCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
