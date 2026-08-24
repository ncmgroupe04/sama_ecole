using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.ResetSchoolData;

/// <summary>
/// Ne vérifie ici que la PRÉSENCE de la confirmation. Sa valeur attendue est comparée dans le
/// Handler : elle accepte aussi le nom de l'établissement, que ce validateur — sans accès à la base —
/// ne peut pas connaître.
/// </summary>
public class ResetSchoolDataCommandValidator : AbstractValidator<ResetSchoolDataCommand>
{
    public ResetSchoolDataCommandValidator()
    {
        RuleFor(c => c.Confirmation)
            .NotEmpty()
            .WithMessage($"Saisissez « {ResetSchoolDataConfirmation.Keyword} » ou le nom de votre établissement pour confirmer.");
    }
}
