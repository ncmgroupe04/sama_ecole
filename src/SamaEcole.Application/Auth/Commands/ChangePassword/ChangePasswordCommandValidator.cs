using SamaEcole.Application.Users.Common;
using FluentValidation;

namespace SamaEcole.Application.Auth.Commands.ChangePassword;

/// <summary>
/// MÊME politique de mot de passe que les deux autres voies (ResetPasswordCommandValidator,
/// ResetUserPasswordCommandValidator) via PasswordPolicy — une seule source de vérité pour « ce
/// qu'est un mot de passe acceptable », quel que soit le chemin qui le fixe.
/// </summary>
public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Le mot de passe actuel est requis.");

        RuleFor(x => x.NewPassword).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(password ?? string.Empty))
            {
                context.AddFailure(error);
            }
        });
    }
}
