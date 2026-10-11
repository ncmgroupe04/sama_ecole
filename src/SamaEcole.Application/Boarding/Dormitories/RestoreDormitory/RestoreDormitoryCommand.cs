using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;

namespace SamaEcole.Application.Boarding.Dormitories.RestoreDormitory;

/// <summary>POST /api/v1/boarding/dormitories/{id}/restore — restaure un pavillon supprimé logiquement.</summary>
public record RestoreDormitoryCommand(Guid Id) : IRequest<Unit>;

public class RestoreDormitoryCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreDormitoryCommand, Unit>
{
    public async Task<Unit> Handle(RestoreDormitoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.Dormitories, schoolId, request.Id, "Un pavillon",
            d => other => other.Name == d.Name, null, cancellationToken);

        return Unit.Value;
    }
}
