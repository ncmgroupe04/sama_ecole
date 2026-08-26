using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Exams.Commands.AssignExamCenter;

public class AssignExamCenterCommandValidator : AbstractValidator<AssignExamCenterCommand>
{
    public AssignExamCenterCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExamCenterName).MaximumLength(150).NoHtml();
        RuleFor(x => x.CandidateNumber).MaximumLength(20).NoHtml();
    }
}
