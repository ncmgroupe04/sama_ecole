using FluentValidation;

namespace SamaEcole.Application.Finance.Queries.GetPayments;

public sealed class GetPaymentsQueryValidator : AbstractValidator<GetPaymentsQuery>
{
    public const int MaxPageSize = 100;

    public GetPaymentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
    }
}
