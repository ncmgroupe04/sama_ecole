using FluentValidation;

namespace SamaEcole.Application.Subscriptions.Queries.GetSubscriptionPayments;

public class GetSubscriptionPaymentsQueryValidator : AbstractValidator<GetSubscriptionPaymentsQuery>
{
    /// <summary>Même raisonnement que GetAuditLogsQueryValidator : sans plafond, ?pageSize=1000000 laisse le client dicter la taille de la réponse.</summary>
    public const int MaxPageSize = 100;

    public GetSubscriptionPaymentsQueryValidator()
    {
        RuleFor(x => x.SchoolId).NotEmpty();
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
    }
}
