using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.LockProduction;

/// <summary>
/// Ne vérifie ici que la PRÉSENCE de la confirmation. Sa valeur attendue est comparée dans le
/// Handler : elle accepte aussi le nom de l'établissement, que ce validateur — sans accès à la base —
/// ne peut pas connaître (même découpage que GoLiveCommandValidator).
/// </summary>
public class LockProductionCommandValidator : AbstractValidator<LockProductionCommand>
{
    public LockProductionCommandValidator()
    {
        RuleFor(c => c.Confirmation)
            .NotEmpty()
            .WithMessage($"Saisissez « {LockProductionConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.");
    }
}
