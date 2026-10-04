using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Uniforms.Commands.CreateUniformVariant;

public class CreateUniformVariantCommandValidator : AbstractValidator<CreateUniformVariantCommand>
{
    public CreateUniformVariantCommandValidator()
    {
        RuleFor(x => x.UniformItemId).NotEmpty();
        RuleFor(x => x.SizeLabel).NotEmpty().MaximumLength(UniformCatalogRules.MaxSizeLabelLength).NoHtml();
    }
}
