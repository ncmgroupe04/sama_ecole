using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Users.Queries.GetUsers;

public class GetUsersQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser)
    : IRequestHandler<GetUsersQuery, IReadOnlyList<UserListItem>>
{
    public async Task<IReadOnlyList<UserListItem>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // La table users n'implémente pas ITenantEntity (SchoolId y est nullable, pour le Super Admin) :
        // pas de Global Query Filter automatique ici, contrairement aux autres tables tenant — filtre
        // explicite, comme ChangeUserStatusCommandHandler (docs/Volume_3_DDS.md §5.2).
        return await dbContext.Users
            .AsNoTracking()
            .Where(u => u.SchoolId == schoolId)
            .OrderBy(u => u.FullName)
            .Select(u => new UserListItem(u.Id, u.FullName, u.Email, u.Role, u.Status, u.Id == currentUser.UserId))
            .ToListAsync(cancellationToken);
    }
}
