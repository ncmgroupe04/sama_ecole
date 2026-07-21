using SamaEcole.Application.Users.Common;
using FluentValidation;

namespace SamaEcole.Application.Auth.Commands.ResetPassword;

/// <summary>
/// MÊME politique de mot de passe que la voie administrative (ResetUserPasswordCommandValidator) via
/// PasswordPolicy : un mot de passe choisi en self-service ne doit pas être plus faible que celui
/// qu'un Directeur peut fixer — une seule source de vérité pour « ce qu'est un mot de passe acceptable ».
///
/// Le JETON n'est validé QUE sur sa présence : sa forme ne se contrôle pas ici. Un jeton mal formé et
/// un jeton inexistant doivent recevoir la même réponse, sinon la distinction renseignerait un
/// attaquant sur la structure attendue (voir ResetPasswordCommandHandler).
/// </summary>
public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Lien de réinitialisation incomplet. Refaites une demande depuis la page de connexion.");

        RuleFor(x => x.NewPassword).Custom((password, context) =>
        {
            foreach (var error in PasswordPolicy.Validate(password ?? string.Empty))
            {
                context.AddFailure(error);
            }
        });
    }
}
