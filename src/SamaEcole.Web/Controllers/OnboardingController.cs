using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Subscriptions.Commands.SelectProfile;
using SamaEcole.Application.Subscriptions.Queries.GetTenantSubscription;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Onboarding & Pricing SaaS — choix du profil et de la tranche d'effectif de l'établissement COURANT.
///
/// Ces routes restent ouvertes pendant l'Onboarding (voir OnboardingRoutingMiddleware et la liste blanche
/// de SubscriptionAwaitingPaymentMiddleware) : ce sont les seules, avec la session et le paiement.
/// « courant » vient du claim JWT, jamais d'un paramètre (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/onboarding")]
[Authorize]
public class OnboardingController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// État de la souscription (statut, profil, tranche, plafonds, modules). Ouvert à tout rôle de l'école :
    /// l'écran s'en sert pour savoir s'il doit encore s'afficher.
    /// </summary>
    [HttpGet("subscription")]
    [ProducesResponseType<TenantSubscriptionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscription(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetTenantSubscriptionQuery(), cancellationToken));

    /// <summary>
    /// Le Directeur choisit son profil et sa tranche : l'école passe de PendingOnboarding à Active et le
    /// quota de la tranche s'applique immédiatement. Une seule fois (409 ONBOARDING_ALREADY_COMPLETED ensuite).
    /// </summary>
    [HttpPost("select-profile")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<TenantSubscriptionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SelectProfile(
        [FromBody] SelectProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SelectProfileCommand(request.Profile, request.Tier), cancellationToken));
}
