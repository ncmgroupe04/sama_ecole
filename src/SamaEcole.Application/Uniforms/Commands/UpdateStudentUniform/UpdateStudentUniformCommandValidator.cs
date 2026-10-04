using FluentValidation;
using SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;

namespace SamaEcole.Application.Uniforms.Commands.UpdateStudentUniform;

public class UpdateStudentUniformCommandValidator : AbstractValidator<UpdateStudentUniformCommand>
{
    public UpdateStudentUniformCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.UniformVariantId).NotEqual(Guid.Empty).When(x => x.UniformVariantId.HasValue);
        RuleFor(x => x.Quantity).InclusiveBetween(1, CreateStudentUniformsCommandValidator.MaxQuantity)
            .WithMessage($"La quantité doit être comprise entre 1 et {CreateStudentUniformsCommandValidator.MaxQuantity}.");
    }
}
