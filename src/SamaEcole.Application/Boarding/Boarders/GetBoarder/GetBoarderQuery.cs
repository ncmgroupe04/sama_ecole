using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Boarders.GetBoarder;

/// <summary>
/// GET /api/v1/boarding/boarders/{id} — fiche complète. Les notes médicales sont masquées (<c>null</c>) pour tout rôle
/// autre que Directeur et Surveillant (spec §5.2).
/// </summary>
public record GetBoarderQuery(Guid Id) : IRequest<BoarderDetailDto>;

public class GetBoarderQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<GetBoarderQuery, BoarderDetailDto>
{
    public Task<BoarderDetailDto> Handle(GetBoarderQuery request, CancellationToken cancellationToken) =>
        BoarderDetailReader.GetAsync(dbContext, request.Id, currentUser.Role, cancellationToken);
}
