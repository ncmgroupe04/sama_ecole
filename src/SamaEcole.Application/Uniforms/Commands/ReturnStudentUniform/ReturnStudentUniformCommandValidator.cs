using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Uniforms.Commands.ReturnStudentUniform;

public class ReturnStudentUniformCommandValidator : AbstractValidator<ReturnStudentUniformCommand>
{
    public ReturnStudentUniformCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(UniformDeliveryPolicy.MaxReasonLength).NoHtml();
    }
}
