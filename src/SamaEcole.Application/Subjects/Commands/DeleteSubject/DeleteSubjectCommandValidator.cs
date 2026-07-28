using FluentValidation;

namespace SamaEcole.Application.Subjects.Commands.DeleteSubject;

public class DeleteSubjectCommandValidator : AbstractValidator<DeleteSubjectCommand>
{
    public DeleteSubjectCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
