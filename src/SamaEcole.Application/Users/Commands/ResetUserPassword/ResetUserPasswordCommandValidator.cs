using SamaEcole.Application.Users.Common;
using FluentValidation;

namespace SamaEcole.Application.Users.Commands.ResetUserPassword;

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.NewPassword).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(password ?? string.Empty))
            {
                context.AddFailure(error);
            }
        });
    }
}
