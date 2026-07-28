using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

public class ApplyStandardFeeCommandValidator : AbstractValidator<ApplyStandardFeeCommand>
{
    public ApplyStandardFeeCommandValidator()
    {
        RuleFor(x => x.FeeCategoryId).NotEmpty();

        // Périmètre facultatif : NULL/vide = toutes les classes. Renseigné, il suit la même règle que
        // le niveau d'une classe (nomenclature LIBRE, jamais une énumération figée) — on borne donc la
        // longueur et on refuse le HTML, sans imposer de liste. Un niveau qui ne désigne aucune classe
        // est rejeté par le handler, qui seul connaît les classes de l'école.
        RuleFor(x => x.Level)
            .MaximumLength(50).NoHtml()
            .When(x => !string.IsNullOrWhiteSpace(x.Level));

        // Zéro est permis (un frais peut être offert). Négatif ne l'est jamais : on facture, on ne
        // rembourse pas via le barème. Le plafond intercepte la faute de frappe — un zéro de trop
        // transformerait 15 000 F en 150 000 F sur toutes les classes d'un coup.
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(0).WithMessage("Le montant ne peut pas être négatif.")
            .LessThanOrEqualTo(100_000_000).WithMessage("Le montant annoncé semble irréaliste.");
    }
}
