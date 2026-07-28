using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.RecordPayment;

/// <summary>
/// Validation de forme du versement (le contrôle de SOLDE — ne pas dépasser le restant dû — est une règle
/// métier tenue dans le Handler, à l'intérieur de la transaction, car il dépend de l'état en base).
/// </summary>
public class RecordPaymentCommandValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentCommandValidator()
    {
        RuleFor(c => c.EnrollmentId)
            .NotEmpty().WithMessage("L'inscription à encaisser est obligatoire.");

        RuleFor(c => c.Amount)
            .GreaterThan(0).WithMessage("Le montant du versement doit être strictement positif.");

        RuleFor(c => c.Method)
            .IsInEnum().WithMessage("Moyen de paiement invalide.");

        RuleFor(c => c.VatRate)
            .InclusiveBetween(0m, 1m).WithMessage("Le taux de TVA doit être compris entre 0 et 1 (ex. 0.18 pour 18 %).")
            .When(c => c.VatRate.HasValue);
    }
}
