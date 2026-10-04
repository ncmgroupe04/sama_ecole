using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.RestoreMention;

/// <summary>POST /api/v1/mentions/{id}/restore — restaure une mention supprimée logiquement.</summary>
public record RestoreMentionCommand(Guid Id) : IRequest<Unit>;

public class RestoreMentionCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreMentionCommand, Unit>
{
    public async Task<Unit> Handle(RestoreMentionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.Mentions, schoolId, request.Id, "Une mention",
            m => other => other.Label == m.Label, beforeRestore: null, cancellationToken);

        return Unit.Value;
    }
}
