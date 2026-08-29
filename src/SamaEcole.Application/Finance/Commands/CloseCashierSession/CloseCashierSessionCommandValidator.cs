using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CloseCashierSession;

public class CloseCashierSessionCommandValidator : AbstractValidator<CloseCashierSessionCommand>
{
    public CloseCashierSessionCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.ActualCashAmount).GreaterThanOrEqualTo(0);

        // Le caractère OBLIGATOIRE du motif quand l'écart est non nul se vérifie dans le Handler
        // (Handle), pas ici : l'écart dépend d'ExpectedCashAmount, lui-même calculé depuis les
        // paiements déjà enregistrés en base — un Validator stateless ne peut pas le connaître.
        RuleFor(x => x.DiscrepancyReason).MaximumLength(500);
    }
}
