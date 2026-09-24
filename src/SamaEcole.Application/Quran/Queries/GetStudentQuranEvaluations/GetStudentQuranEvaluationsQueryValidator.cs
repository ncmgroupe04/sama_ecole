using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;

public class GetStudentQuranEvaluationsQueryValidator : AbstractValidator<GetStudentQuranEvaluationsQuery>
{
    public GetStudentQuranEvaluationsQueryValidator()
    {
        RuleFor(q => q.StudentId).NotEmpty();
    }
}
