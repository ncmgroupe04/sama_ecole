using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.ChangeSchoolStatus;

public class ChangeSchoolStatusCommandValidator : AbstractValidator<ChangeSchoolStatusCommand>
{
    public ChangeSchoolStatusCommandValidator()
    {
        RuleFor(c => c.SchoolId).NotEmpty();

        RuleFor(c => c.Status)
            .IsInEnum().WithMessage("Statut inconnu : valeurs admises Active, Suspended, Blocked.");

        // Même exigence que ChangeUserStatusCommandValidator (JGK-A05) : un motif vide ou « ok » ne
        // documente rien, et l'audit doit rester exploitable des mois plus tard.
        RuleFor(c => c.Reason)
            .NotEmpty().WithMessage("Le motif est obligatoire.")
            .MinimumLength(5).WithMessage("Le motif doit être explicite (5 caractères minimum).")
            .MaximumLength(500)
            .NoHtml();
    }
}
