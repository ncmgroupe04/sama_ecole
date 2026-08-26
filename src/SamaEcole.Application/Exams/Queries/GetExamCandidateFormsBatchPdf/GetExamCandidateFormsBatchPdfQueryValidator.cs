using FluentValidation;

namespace SamaEcole.Application.Exams.Queries.GetExamCandidateFormsBatchPdf;

public class GetExamCandidateFormsBatchPdfQueryValidator : AbstractValidator<GetExamCandidateFormsBatchPdfQuery>
{
    public GetExamCandidateFormsBatchPdfQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => x.ExamSessionId.HasValue || x.ClassroomId.HasValue)
            .WithMessage("Au moins un filtre (examSessionId ou classroomId) est requis pour une impression par lot.");
    }
}
