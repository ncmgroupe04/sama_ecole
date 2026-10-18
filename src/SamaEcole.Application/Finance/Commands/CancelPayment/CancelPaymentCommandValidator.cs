using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CancelPayment;

/// <summary>
/// Validation de forme. Le motif est obligatoire : il est affiché sur le reçu annulé et dans l'historique
/// financier, c'est ce qui permet de comprendre une correction des mois plus tard. Borné à 500 caractères,
/// la largeur de <c>payments.CancellationReason</c>.
/// </summary>
public class CancelPaymentCommandValidator : AbstractValidator<CancelPaymentCommand>
{
    public CancelPaymentCommandValidator()
    {
        RuleFor(c => c.PaymentId)
            .NotEmpty().WithMessage("Le paiement à annuler est obligatoire.");

        RuleFor(c => c.Outcome)
            .IsInEnum().WithMessage("Destination de l'argent invalide.");

        RuleFor(c => c.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
                .WithMessage("Le motif de l'annulation est obligatoire.")
            .Must(reason => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length >= 3)
                .WithMessage("Le motif de l'annulation est trop court (3 caractères minimum).")
            .MaximumLength(500).WithMessage("Le motif de l'annulation ne peut pas dépasser 500 caractères.");
    }
}
