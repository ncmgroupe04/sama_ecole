using FluentValidation;

namespace SamaEcole.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Ne valide que la FORME de l'adresse — jamais son existence, qui reste indécelable de l'extérieur
/// (voir ForgotPasswordCommandHandler). Une saisie manifestement invalide est refusée en 422 sans rien
/// révéler : le message porte sur le format, identique que l'adresse existe ou non.
/// </summary>
public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("L'adresse e-mail est obligatoire.")
            .MaximumLength(256)
            .EmailAddress().WithMessage("Adresse e-mail invalide.");
    }
}
