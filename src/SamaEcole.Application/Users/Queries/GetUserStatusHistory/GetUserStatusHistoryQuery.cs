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

public record UserStatusHistoryEntry(
    EntityStatus PreviousStatus,
    EntityStatus NewStatus,
    string Reason,
    Guid ChangedByUserId,
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
        var entries = await dbContext.UserStatusHistory
            .AsNoTracking()
            .Where(h => h.UserId == request.UserId)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new UserStatusHistoryEntry(
                h.PreviousStatus, h.NewStatus, h.Reason, h.ChangedByUserId, h.ChangedAt))
            .ToListAsync(cancellationToken);

        return entries;
    }
}
