using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Exams.Commands.CreateExamSession;

public class CreateExamSessionCommandValidator : AbstractValidator<CreateExamSessionCommand>
{
    public CreateExamSessionCommandValidator()
    {
        RuleFor(x => x.SchoolYearId).NotEmpty();
        RuleFor(x => x.Series).MaximumLength(20).NoHtml();
        RuleFor(x => x.CenterName).MaximumLength(150).NoHtml();
    }
}
