using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.AssignTeacher;

public class AssignTeacherCommandValidator : AbstractValidator<AssignTeacherCommand>
{
    public AssignTeacherCommandValidator()
    {
        RuleFor(x => x.TeacherId).NotEmpty();
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
    }
}
