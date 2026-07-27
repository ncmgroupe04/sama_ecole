using FluentValidation;

namespace SamaEcole.Application.Platform.Commands.TopUpSmsCredits;

public class TopUpSmsCreditsCommandValidator : AbstractValidator<TopUpSmsCreditsCommand>
{
    public TopUpSmsCreditsCommandValidator()
    {
        RuleFor(x => x.SchoolId).NotEmpty();

        // Strictement positif : un crédit négatif serait un DÉBIT déguisé, qui contournerait la
        // comptabilité des envois (chaque consommation doit laisser une ligne dans sms_messages).
        RuleFor(x => x.Segments)
            .GreaterThan(0).WithMessage("Le nombre de segments à créditer doit être positif.")
            .LessThanOrEqualTo(1_000_000).WithMessage("Crédit trop important : vérifiez la saisie.");
    }
}
