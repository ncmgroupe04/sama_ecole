using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Users.Queries.GetUserStatusHistory;

/// <summary>
/// GET /users/{userId}/status-history — ticket JGK-A05, critère « l'historique des changements de
/// statut est consultable ».
/// </summary>
public record GetUserStatusHistoryQuery(Guid UserId) : IRequest<IReadOnlyList<UserStatusHistoryEntry>>;

/// <param name="ChangedByFullName">Nom de l'auteur, joint depuis la table des comptes (même idiome que GetFeeHistoryQuery).</param>
public record UserStatusHistoryEntry(
    EntityStatus PreviousStatus,
    EntityStatus NewStatus,
    string Reason,
    Guid ChangedByUserId,
    string ChangedByFullName,
    DateTimeOffset ChangedAt);

public class GetUserStatusHistoryQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetUserStatusHistoryQuery, IReadOnlyList<UserStatusHistoryEntry>>
{
    public async Task<IReadOnlyList<UserStatusHistoryEntry>> Handle(
        GetUserStatusHistoryQuery request,
        CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS cantonnent d'office la lecture à l'école du JWT :
        // inutile de filtrer sur SchoolId ici, et surtout impossible de l'oublier.
        var entries = await (
            from h in dbContext.UserStatusHistory.AsNoTracking()
            join actor in dbContext.Users.AsNoTracking() on h.ChangedByUserId equals actor.Id
            where h.UserId == request.UserId
            orderby h.ChangedAt descending
            select new UserStatusHistoryEntry(
                h.PreviousStatus, h.NewStatus, h.Reason, h.ChangedByUserId, actor.FullName, h.ChangedAt))
            .ToListAsync(cancellationToken);

        return entries;
    }
}
