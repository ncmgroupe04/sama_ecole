using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Platform.Commands.GrantComplimentaryAccess;

/// <summary>
/// POST /admin/platform/schools/{schoolId}/complimentary-access — bouton « Offrir un accès » de
/// l'écran Abonnements &amp; Facturation. Réservé au Super Admin.
///
/// N'échange AUCUN argent : ce n'est pas un paiement, donc AGENTS.md règle #11 (confirmation par
/// webhook signé uniquement) ne s'applique pas ici — l'abonnement passe directement Actif, avec
/// l'échéance choisie, exactement comme le ferait un paiement confirmé. `subscriptions` étant sous
/// RLS (WITH CHECK sur SchoolId, migration AddSubscriptionProvisioning), la modification passe par la
/// fonction SECURITY DEFINER grant_complimentary_subscription (ISubscriptionAdminStore) plutôt que
/// par un UPDATE EF direct, qu'un Super Admin sans SchoolId ne pourrait pas satisfaire.
/// </summary>
public record GrantComplimentaryAccessCommand : IRequest
{
    public required Guid SchoolId { get; init; }
    public required SubscriptionPlan Plan { get; init; }
    public required int DurationMonths { get; init; }
}

public class GrantComplimentaryAccessCommandHandler(
    ISubscriptionAdminStore subscriptionAdminStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<GrantComplimentaryAccessCommandHandler> logger)
    : IRequestHandler<GrantComplimentaryAccessCommand>
{
    public async Task Handle(GrantComplimentaryAccessCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var now = timeProvider.GetUtcNow();
        var expiresAt = DateOnly.FromDateTime(now.UtcDateTime).AddMonths(request.DurationMonths);

        var granted = await subscriptionAdminStore.GrantComplimentaryAccessAsync(
            request.SchoolId, request.Plan, expiresAt, promoCodeId: null, cancellationToken);

        if (!granted)
        {
            // La garde WHERE de grant_complimentary_subscription n'a touché aucune ligne : cette école
            // n'a aucun abonnement à modifier (voir CreateSchoolCommand pour l'amorçage initial).
            throw new KeyNotFoundException("Aucun abonnement associé à cet établissement.");
        }

        // Attribuée à l'école CIBLE (même raisonnement que ImpersonateSchoolCommandHandler) : l'acteur
        // Super Admin n'a pas de SchoolId propre à qui imputer l'entrée.
        await auditLogStore.AppendAsync(
            request.SchoolId, superAdminId, "Subscriptions", "GrantComplimentaryAccess",
            success: true, failureReason: null, currentUser.IpAddress, now, cancellationToken);

        logger.LogInformation(
            "Super Admin {SuperAdminId} a offert un accès {Plan} jusqu'au {ExpiresAt} à l'établissement {SchoolId}.",
            superAdminId, request.Plan, expiresAt, request.SchoolId);
    }
}
