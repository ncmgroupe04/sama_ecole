using FluentValidation;

namespace SamaEcole.Application.Platform.Queries.GetPlatformActivity;

public class GetPlatformActivityQueryValidator : AbstractValidator<GetPlatformActivityQuery>
{
    /// <summary>Même raisonnement que GetAuditLogsQueryValidator : sans plafond, ?pageSize=1000000 laisse le client dicter la taille de la réponse.</summary>
    public const int MaxPageSize = 100;

    public GetPlatformActivityQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
    }
}
