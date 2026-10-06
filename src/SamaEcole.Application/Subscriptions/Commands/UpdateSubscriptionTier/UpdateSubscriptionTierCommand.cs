using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Commands.UpdateSubscriptionTier;

/// <summary>
/// Changement de tranche d'effectif d'une école par le Super Admin. CONTRAT seulement : le handler est
/// livré avec l'écran Super Admin — il devra écrire par une fonction SECURITY DEFINER, le Super Admin
/// n'ayant aucun SchoolId de session sous la RLS (voir <c>SubscriptionAdminStore</c>).
///
/// Ici <see cref="SchoolId"/> est légitime en paramètre : c'est précisément l'acte d'un opérateur plateforme
/// qui cible une école, contrairement à <see cref="SelectProfileRequest"/> où l'école vient du JWT.
/// </summary>
public record UpdateSubscriptionTierCommand : IRequest<TenantSubscriptionDto>
{
    public required Guid SchoolId { get; init; }
    public required StudentQuotaTier Tier { get; init; }

    /// <summary>Plafond sur mesure — OBLIGATOIRE pour <see cref="StudentQuotaTier.Tier4_Custom"/>, ignoré sinon.</summary>
    public int? CustomMaxStudentLimit { get; init; }
}
