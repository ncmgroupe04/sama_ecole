using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Teachers.Queries.GetTeachers;

public class GetTeachersQueryValidator : AbstractValidator<GetTeachersQuery>
{
    public const int MaxPageSize = 100;

    public GetTeachersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(100).NoHtml();
    }
}
