using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.SchoolYears.Deletion;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.SchoolYears.Commands.UnlinkSchoolYearDependencies;

/// <summary>
/// Rupture EXPLICITE des liens non financiers d'une année scolaire (spec 2026-10-17, §2.1). Ne supprime PAS l'année :
/// c'est DeleteSchoolYearCommand, qui ne rompt jamais rien en mode réel. Chaque lien est archivé (rien n'est effacé).
/// En mode test il n'y a rien à rompre (l'effacement définitif emporte tout) : la commande renvoie l'analyse sans rien faire.
/// IAuditableRequest : la rupture est tracée.
/// </summary>
public record UnlinkSchoolYearDependenciesCommand(Guid SchoolYearId) : IRequest<DeletionImpact>, IAuditableRequest;

public class UnlinkSchoolYearDependenciesCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser)
    : IRequestHandler<UnlinkSchoolYearDependenciesCommand, DeletionImpact>
{
    public async Task<DeletionImpact> Handle(UnlinkSchoolYearDependenciesCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Double garde du rôle (comme DeleteSchoolYearCommandHandler) : archiver les données d'un exercice est réservé au Directeur.
        if (currentUser.Role != Role.Directeur)
        {
            throw new UnauthorizedAccessException("Seul le Directeur peut supprimer une année scolaire.");
        }

        if (await SchoolYearDeletionPolicy.IsTestModeAsync(dbContext, schoolId, cancellationToken))
        {
            return await SchoolYearDeletionPolicy.AnalyzeAsync(dbContext, schoolId, request.SchoolYearId, cancellationToken);
        }

        return await dbContext.ExecuteInTransactionAsync(
            ct => EntityDeletionAnalyzer.UnlinkAsync(
                dbContext, SchoolYearDeletionPolicy.Policy, request.SchoolYearId, actorId.ToString(), ct),
            cancellationToken);
    }
}
