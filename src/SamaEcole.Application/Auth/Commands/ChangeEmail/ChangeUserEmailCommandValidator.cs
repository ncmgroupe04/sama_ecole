using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Auth.Commands.ChangeEmail;

/// <summary>
/// Même forme que CreateUserCommandValidator pour le format d'e-mail, et même exigence que
/// ChangePasswordCommandValidator pour la preuve d'identité (mot de passe actuel requis).
/// </summary>
public class ChangeUserEmailCommandValidator : AbstractValidator<ChangeUserEmailCommand>
{
    public ChangeUserEmailCommandValidator()
    {
        RuleFor(x => x.NewEmail).NotEmpty().EmailAddress().MaximumLength(256).NoHtml();

        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Le mot de passe actuel est requis.");
    }
}
