using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Exams.Commands.UpdateExamSession;

public class UpdateExamSessionCommandValidator : AbstractValidator<UpdateExamSessionCommand>
{
    public UpdateExamSessionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CenterName).MaximumLength(150).NoHtml();
        RuleFor(x => x.Status).IsInEnum();
    }
}
