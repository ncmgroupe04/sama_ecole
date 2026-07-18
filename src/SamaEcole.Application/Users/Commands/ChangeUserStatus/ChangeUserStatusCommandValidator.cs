using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Users.Commands.ChangeUserStatus;

public class ChangeUserStatusCommandValidator : AbstractValidator<ChangeUserStatusCommand>
{
    public ChangeUserStatusCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();

        RuleFor(c => c.Status)
            .IsInEnum().WithMessage("Statut inconnu : valeurs admises Active, Suspended, Blocked.");

        // Un motif vide ou « ok » ne documente rien. L'historique doit rester exploitable des mois
        // plus tard, quand plus personne ne se souvient du contexte (ticket JGK-A05).
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}
