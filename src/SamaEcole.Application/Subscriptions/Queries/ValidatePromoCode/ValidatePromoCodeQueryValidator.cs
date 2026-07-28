using FluentValidation;

namespace SamaEcole.Application.Subscriptions.Queries.ValidatePromoCode;

public class ValidatePromoCodeQueryValidator : AbstractValidator<ValidatePromoCodeQuery>
{
    public ValidatePromoCodeQueryValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.BillingPeriod).IsInEnum();
    }
}
