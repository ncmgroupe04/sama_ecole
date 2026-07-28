using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Registration.Queries.GetRegistrationRequests;

/// <summary>
/// Ticket JGK-I03. La projection Select est, comme pour I02, la garantie que le hash du mot de passe ne
/// quitte jamais la base pour cette requête : il n'est pas lu, plutôt que lu puis omis du DTO.
/// </summary>
public class GetRegistrationRequestsHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetRegistrationRequestsQuery, IReadOnlyList<RegistrationRequestListItem>>
{
    public async Task<IReadOnlyList<RegistrationRequestListItem>> Handle(
        GetRegistrationRequestsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.SchoolRegistrationRequests.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(r => r.Status == status);
        }

        return await query
            // Les plus récentes d'abord : le Super Admin traite en priorité les demandes fraîches.
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new RegistrationRequestListItem(
                r.Id,
                r.TrackingReference,
                r.SchoolName,
                r.SchoolAddress,
                r.City,
                r.Region,
                r.EstimatedStudentCount,
                r.RequestedPlan,
                r.DirectorFullName,
                r.DirectorEmail,
                r.DirectorPhone,
                r.Status,
                r.CreatedAt,
                r.ReviewedAt,
                r.RejectionReason,
                r.CreatedSchoolId))
            .ToListAsync(cancellationToken);
    }
}
