using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Uniforms.Commands.CreateUniformItem;

public class CreateUniformItemCommandValidator : AbstractValidator<CreateUniformItemCommand>
{
    public CreateUniformItemCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(UniformCatalogRules.MaxItemNameLength).NoHtml();
        RuleFor(x => x.Notes).MaximumLength(UniformCatalogRules.MaxNotesLength).NoHtml();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.Cycle).IsInEnum().When(x => x.Cycle.HasValue);

        RuleFor(x => x.QuantityIncluded)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Un frais couvre au moins une unité.");
    }
}
