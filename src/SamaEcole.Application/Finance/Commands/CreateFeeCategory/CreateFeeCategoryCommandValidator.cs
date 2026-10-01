using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

public class CreateFeeCategoryCommandValidator : AbstractValidator<CreateFeeCategoryCommand>
{
    public CreateFeeCategoryCommandValidator()
    {
        // NoHtml laisse passer « Frais d'inscription » (le MOT script n'est pas bloqué — voir SafeTextValidation).
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60).NoHtml();

        // La pension a déjà son propre choix explicite (régime d'hébergement + IncludeBoardingFee) : lui
        // ajouter une case « optionnel » créerait deux mécanismes concurrents pour la même décision.
        RuleFor(x => x.IsOptional).Equal(false)
            .When(x => x.IsBoardingFee)
            .WithMessage("Une catégorie de pension ne peut pas être optionnelle : son inclusion se choisit avec le régime d'hébergement.");
    }
}
