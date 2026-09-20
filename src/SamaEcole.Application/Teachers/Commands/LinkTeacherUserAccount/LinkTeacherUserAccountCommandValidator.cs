using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.LinkTeacherUserAccount;

public class LinkTeacherUserAccountCommandValidator : AbstractValidator<LinkTeacherUserAccountCommand>
{
    public LinkTeacherUserAccountCommandValidator()
    {
        RuleFor(x => x.TeacherId).NotEmpty();
    }
}
