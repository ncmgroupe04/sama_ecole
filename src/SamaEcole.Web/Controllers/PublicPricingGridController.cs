using SamaEcole.Application.Subscriptions.Pricing;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Grille tarifaire par tranche d'effectif, en LECTURE PUBLIQUE : le formulaire d'inscription l'affiche avant
/// que l'école n'existe. Aucune donnée d'école n'y figure — uniquement les prix publiés. Le montant réellement
/// facturé n'est JAMAIS celui que le client renvoie : il est recalculé côté serveur (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/public/pricing-grid")]
[AllowAnonymous]
[EnableRateLimiting(SensitiveEndpointRateLimiting.PublicDirectoryPolicyName)]
public class PublicPricingGridController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PublicPricingGridDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPublicPricingGridQuery(), cancellationToken));
}
