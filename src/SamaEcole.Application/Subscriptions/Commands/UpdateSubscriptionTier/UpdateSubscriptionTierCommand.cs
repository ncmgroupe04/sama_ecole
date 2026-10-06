using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;

namespace SamaEcole.Application.Subscriptions.Commands.UpdateSubscriptionTier;

/// <summary>
/// PUT /admin/platform/schools/{schoolId}/tenant-subscription — le Super Admin change la tranche d'effectif
/// (y compris le passage en sur-mesure, <see cref="StudentQuotaTier.Tier4_Custom"/>) et/ou le statut
/// (suspension, expiration, réactivation) de la souscription commerciale d'une école. Au moins l'un des deux.
///
/// Ici <see cref="SchoolId"/> est légitime en paramètre : c'est l'acte d'un opérateur plateforme qui cible une
/// école, contrairement à <see cref="SelectProfileRequest"/> où l'école vient du JWT. La modification passe par
/// une fonction SECURITY DEFINER (ITenantSubscriptionAdminStore) : sous RLS, le Super Admin n'a aucun SchoolId
/// de session.
/// </summary>
public record UpdateSubscriptionTierCommand : IRequest<TenantSubscriptionDto>
{
    public required Guid SchoolId { get; init; }

    /// <summary>Nouvelle tranche ; <c>null</c> = inchangée.</summary>
    public StudentQuotaTier? Tier { get; init; }

    /// <summary>
    /// Plafond sur mesure — OBLIGATOIRE avec <see cref="StudentQuotaTier.Tier4_Custom"/>, interdit sinon.
    /// <see cref="int.MaxValue"/> = illimité. La tolérance (Soft cap) est calculée (voir StudentQuotaDefaults).
    /// </summary>
    public int? CustomMaxStudentLimit { get; init; }

    /// <summary>
    /// Nouveau statut ; <c>null</c> = inchangé. <see cref="TenantSubscriptionStatus.PendingOnboarding"/> n'est
    /// jamais assignable : c'est l'état de naissance, quitté par le choix du Directeur.
    /// </summary>
    public TenantSubscriptionStatus? Status { get; init; }
}

public class UpdateSubscriptionTierCommandHandler(
    ITenantSubscriptionAdminStore store,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<UpdateSubscriptionTierCommandHandler> logger)
    : IRequestHandler<UpdateSubscriptionTierCommand, TenantSubscriptionDto>
{
    public async Task<TenantSubscriptionDto> Handle(UpdateSubscriptionTierCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var current = await store.GetAsync(request.SchoolId, cancellationToken)
            ?? throw new NotFoundException("Aucune souscription n'est rattachée à cet établissement.");

        // Une école qui n'a pas encore choisi son profil porte des valeurs PROVISOIRES (voir
        // provision_tenant_subscription) : y poser une tranche ou la réactiver reviendrait à valider un profil
        // que personne n'a choisi — et le choix du Directeur écraserait la tranche de toute façon.
        if (current.Status == nameof(TenantSubscriptionStatus.PendingOnboarding))
        {
            throw new BusinessRuleException(
                "Cet établissement n'a pas encore choisi son profil et sa tranche. Attendez la fin de son Onboarding.",
                "ONBOARDING_NOT_COMPLETED");
        }

        int? max = null;
        int? soft = null;

        if (request.Tier is { } tier)
        {
            (max, soft) = tier == StudentQuotaTier.Tier4_Custom
                ? StudentQuotaDefaults.ForCustom(request.CustomMaxStudentLimit!.Value)
                : StudentQuotaDefaults.For(tier);
        }

        var updated = await store.UpdateAsync(
            request.SchoolId, request.Tier, max, soft, request.Status, superAdminId, cancellationToken)
            ?? throw new NotFoundException("Aucune souscription n'est rattachée à cet établissement.");

        // Attribuée à l'école CIBLE (même raisonnement que GrantComplimentaryAccessCommandHandler) : l'acteur
        // Super Admin n'a pas de SchoolId propre à qui imputer l'entrée.
        await auditLogStore.AppendAsync(
            request.SchoolId, superAdminId, "Subscriptions", "UpdateSubscriptionTier",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Super Admin {SuperAdminId} a modifié la souscription de l'établissement {SchoolId} : " +
            "tranche {OldTier}→{NewTier}, plafond {OldMax}→{NewMax}, statut {OldStatus}→{NewStatus}.",
            superAdminId, request.SchoolId,
            current.StudentQuotaTier, updated.StudentQuotaTier,
            current.MaxStudentLimit, updated.MaxStudentLimit,
            current.Status, updated.Status);

        return updated;
    }
}
