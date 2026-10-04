using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Uniforms.Commands.UpdateUniformVariant;

public class UpdateUniformVariantCommandValidator : AbstractValidator<UpdateUniformVariantCommand>
{
    public UpdateUniformVariantCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.SizeLabel).NotEmpty().MaximumLength(UniformCatalogRules.MaxSizeLabelLength).NoHtml();
    }
}
