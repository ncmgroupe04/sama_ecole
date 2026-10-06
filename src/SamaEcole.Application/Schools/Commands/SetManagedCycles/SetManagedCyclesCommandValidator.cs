using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.SetManagedCycles;

public class SetManagedCyclesCommandValidator : AbstractValidator<SetManagedCyclesCommand>
{
    public SetManagedCyclesCommandValidator()
    {
        // TryParse refuse la liste vide, un nom inconnu (« Creche » n'est pas un cycle), un nombre et un doublon.
        RuleFor(c => c.Cycles)
            .Must(cycles => ManagedCycleSet.TryParse(cycles) is not null)
            .WithMessage("Indiquez au moins un cycle, sans doublon, parmi Maternelle, Primaire, College et Lycee.");
    }
}
