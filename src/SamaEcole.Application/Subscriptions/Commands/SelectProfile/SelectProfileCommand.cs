using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions.Commands.SelectProfile;

/// <summary>
/// POST /onboarding/select-profile — le Directeur choisit son profil ET sa tranche d'effectif, ce qui
/// sort l'école de <see cref="TenantSubscriptionStatus.PendingOnboarding"/> : la souscription devient
/// <c>Active</c> et le quota de la tranche s'applique immédiatement. Une seule fois : un changement de
/// tranche ultérieur est l'affaire du Super Admin (<c>UpdateSubscriptionTierCommand</c>).
///
/// Même corps que <see cref="SelectProfileRequest"/>. Aucun SchoolId : l'école vient du JWT
/// (AGENTS.md règle #10).
/// </summary>
public record SelectProfileCommand(ProfileType Profile, StudentQuotaTier Tier) : IRequest<TenantSubscriptionDto>;
