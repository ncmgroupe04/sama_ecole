using FluentValidation;

namespace SamaEcole.Application.Students.Commands.DeleteStudent;

public class DeleteStudentCommandValidator : AbstractValidator<DeleteStudentCommand>
{
    public DeleteStudentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
