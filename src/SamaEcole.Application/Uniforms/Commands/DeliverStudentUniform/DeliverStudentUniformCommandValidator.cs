using FluentValidation;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Uniforms.Commands.DeliverStudentUniform;

public class DeliverStudentUniformCommandValidator : AbstractValidator<DeliverStudentUniformCommand>
{
    public DeliverStudentUniformCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NotBilledReason).MaximumLength(UniformDeliveryPolicy.MaxReasonLength).NoHtml();
    }
}
