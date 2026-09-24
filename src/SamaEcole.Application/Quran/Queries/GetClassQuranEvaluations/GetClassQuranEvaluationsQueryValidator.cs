using FluentValidation;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;

public class GetClassQuranEvaluationsQueryValidator : AbstractValidator<GetClassQuranEvaluationsQuery>
{
    public GetClassQuranEvaluationsQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty();
    }
}
