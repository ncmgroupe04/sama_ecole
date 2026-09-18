using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.RevertToTest;

/// <summary>
/// Ne vérifie ici que la PRÉSENCE de la confirmation. Sa valeur attendue est comparée dans le
/// Handler : elle accepte aussi le nom de l'établissement, que ce validateur — sans accès à la base —
/// ne peut pas connaître (même découpage que GoLiveCommandValidator).
/// </summary>
public class RevertToTestCommandValidator : AbstractValidator<RevertToTestCommand>
{
    public RevertToTestCommandValidator()
    {
        RuleFor(c => c.Confirmation)
            .NotEmpty()
            .WithMessage($"Saisissez « {RevertToTestConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.");
    }
}
