using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.RestoreFeeCategory;

/// <summary>POST /api/v1/fee-categories/{id}/restore — restaure une catégorie de frais supprimée logiquement.</summary>
public record RestoreFeeCategoryCommand(Guid Id) : IRequest<Unit>;

public class RestoreFeeCategoryCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<RestoreFeeCategoryCommand, Unit>
{
    public async Task<Unit> Handle(RestoreFeeCategoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Seule la catégorie revient : les lignes de barème (class_fees) supprimées avec elle ne sont PAS
        // réactivées implicitement (conception §3.2, aucune fusion) — le Directeur les ressaisit ou les restaure.
        // Les lignes d'inscription déjà validées restent figées et ne sont pas touchées.
        await SoftDeleteLifecycle.RestoreAsync(
            dbContext, dbContext.FeeCategories, schoolId, request.Id, "Une catégorie de frais",
            c => other => other.Name == c.Name, beforeRestore: null, cancellationToken);

        return Unit.Value;
    }
}
