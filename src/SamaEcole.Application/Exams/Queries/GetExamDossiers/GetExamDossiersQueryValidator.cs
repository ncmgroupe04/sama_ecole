using FluentValidation;

namespace SamaEcole.Application.Exams.Queries.GetExamDossiers;

public class GetExamDossiersQueryValidator : AbstractValidator<GetExamDossiersQuery>
{
    public GetExamDossiersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
