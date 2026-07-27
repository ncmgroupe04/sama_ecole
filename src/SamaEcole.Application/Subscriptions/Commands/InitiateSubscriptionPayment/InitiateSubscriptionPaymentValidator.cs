using FluentValidation;

namespace SamaEcole.Application.Subscriptions.Commands.InitiateSubscriptionPayment;

public class InitiateSubscriptionPaymentValidator : AbstractValidator<InitiateSubscriptionPaymentCommand>
{
    public InitiateSubscriptionPaymentValidator()
    {
        RuleFor(x => x.SchoolId).NotEmpty();
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.BillingPeriod).IsInEnum();
        RuleFor(x => x.PromoCode).MaximumLength(32);
    }
}
