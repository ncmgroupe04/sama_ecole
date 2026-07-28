using FluentValidation;

namespace SamaEcole.Application.Auth.Commands.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // On valide la FORME, jamais la robustesse du mot de passe : à la connexion, exiger 12
        // caractères révélerait la politique appliquée aux comptes et n'apporte rien. La politique
        // de mot de passe (Volume_7 §2) s'applique à la CRÉATION du compte.
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(255);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}