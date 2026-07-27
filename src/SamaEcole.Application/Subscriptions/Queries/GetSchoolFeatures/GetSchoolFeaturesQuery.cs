using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subscriptions.Queries.GetSchoolFeatures;

/// <summary>
/// GET /subscriptions/features — ce que la formule de MON école inclut. Alimente le masquage et les
/// badges « Premium » de l'interface.
///
/// La liste est dérivée de la MÊME matrice <see cref="PlanFeatures"/> que
/// FeatureAuthorizationHandler : l'interface ne peut donc pas proposer un bouton que l'API refusera.
/// Elle reste un CONFORT D'AFFICHAGE — c'est [RequireFeature] côté serveur qui protège réellement,
/// jamais ce que le client choisit d'afficher (même principe que le x-show des écrans Super Admin).
/// </summary>
public record GetSchoolFeaturesQuery : IRequest<SchoolFeaturesDto>;

public record SchoolFeaturesDto(string Plan, IReadOnlyList<string> Features);

public class GetSchoolFeaturesQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetSchoolFeaturesQuery, SchoolFeaturesDto>
{
    public async Task<SchoolFeaturesDto> Handle(
        GetSchoolFeaturesQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var plan = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => (SubscriptionPlan?)s.Plan)
            .SingleOrDefaultAsync(cancellationToken);

        // Pas d'abonnement : aucune fonctionnalité optionnelle, même refus par défaut que
        // FeatureAuthorizationHandler — l'interface et l'API doivent dire la même chose.
        if (plan is not { } currentPlan)
        {
            return new SchoolFeaturesDto(Plan: string.Empty, Features: []);
        }

        return new SchoolFeaturesDto(
            currentPlan.ToString(),
            PlanFeatures.For(currentPlan).Select(f => f.ToString()).ToList());
    }
}
