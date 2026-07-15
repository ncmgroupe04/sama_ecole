using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

public class ApplyStandardFeeCommandValidator : AbstractValidator<ApplyStandardFeeCommand>
{
    public ApplyStandardFeeCommandValidator()
    {
        RuleFor(x => x.FeeCategoryId).NotEmpty();

        // Zéro est permis (un frais peut être offert). Négatif ne l'est jamais : on facture, on ne
        // rembourse pas via le barème. Le plafond intercepte la faute de frappe — un zéro de trop
        // transformerait 15 000 F en 150 000 F sur toutes les classes d'un coup.
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Le montant ne peut pas être négatif.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Le montant annoncé semble irréaliste.");
    }
}
