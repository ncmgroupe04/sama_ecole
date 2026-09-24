using FluentValidation;

namespace SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;

public class GetGradeSheetPdfQueryValidator : AbstractValidator<GetGradeSheetPdfQuery>
{
    public GetGradeSheetPdfQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty();
        RuleFor(q => q.SubjectId).NotEmpty();
        RuleFor(q => q.TermId).NotEmpty();
        RuleFor(q => q.EvaluationType).IsInEnum().WithMessage("Type d'évaluation invalide.");
    }
}
