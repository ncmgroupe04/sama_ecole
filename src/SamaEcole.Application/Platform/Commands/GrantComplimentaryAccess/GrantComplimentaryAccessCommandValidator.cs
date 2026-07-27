using FluentValidation;

namespace SamaEcole.Application.Platform.Commands.GrantComplimentaryAccess;

public class GrantComplimentaryAccessCommandValidator : AbstractValidator<GrantComplimentaryAccessCommand>
{
    public GrantComplimentaryAccessCommandValidator()
    {
        RuleFor(x => x.SchoolId).NotEmpty();
        RuleFor(x => x.DurationMonths).GreaterThan(0).LessThanOrEqualTo(60)
            .WithMessage("La durée doit être comprise entre 1 et 60 mois.");
    }
}
