using FluentValidation;

namespace SamaEcole.Application.Grades.Queries.GetClassGrades;

public class GetClassGradesQueryValidator : AbstractValidator<GetClassGradesQuery>
{
    public GetClassGradesQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty();
        RuleFor(q => q.SubjectId).NotEmpty();
        RuleFor(q => q.TermId).NotEmpty();
    }
}
