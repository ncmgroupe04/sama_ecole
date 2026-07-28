using FluentValidation;

namespace SamaEcole.Application.AuditLogs.Queries.GetAuditLogs;

public class GetAuditLogsQueryValidator : AbstractValidator<GetAuditLogsQuery>
{
    /// <summary>Même raisonnement que GetStudentsQueryValidator : sans plafond, ?pageSize=1000000 laisse le client dicter la taille de la réponse.</summary>
    public const int MaxPageSize = 100;

    public GetAuditLogsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);
        RuleFor(x => x.Module).MaximumLength(50);
    }
}
