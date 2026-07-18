using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

public class CreateFeeCategoryCommandValidator : AbstractValidator<CreateFeeCategoryCommand>
{
    public CreateFeeCategoryCommandValidator()
    {
        // NoHtml laisse passer « Frais d'inscription » (le MOT script n'est pas bloqué — voir SafeTextValidation).
        RuleFor(x => x.Name).NotEmpty().MaximumLength(60).NoHtml();
    }
}
