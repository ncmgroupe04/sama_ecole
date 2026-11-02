using FluentValidation;

namespace SamaEcole.Application.Platform.Commands.GrantComplimentaryAccess;

public class GrantComplimentaryAccessCommandValidator : AbstractValidator<GrantComplimentaryAccessCommand>
{
    public GrantComplimentaryAccessCommandValidator()
    {
        RuleFor(x => x.SchoolId).NotEmpty();

        RuleFor(x => x)
            .Must(x => x.DurationMonths.HasValue != x.DurationDays.HasValue)
            .WithName("Durée")
            .WithMessage("Indiquez une durée en mois ou en jours, pas les deux.");

        RuleFor(x => x.DurationMonths).GreaterThan(0).LessThanOrEqualTo(60)
            .When(x => x.DurationMonths.HasValue)
            .WithMessage("La durée doit être comprise entre 1 et 60 mois.");

        // 1 825 jours = 5 ans, l'équivalent du plafond de 60 mois.
        RuleFor(x => x.DurationDays).GreaterThan(0).LessThanOrEqualTo(1825)
            .When(x => x.DurationDays.HasValue)
            .WithMessage("La durée doit être comprise entre 1 et 1825 jours.");
    }
}
