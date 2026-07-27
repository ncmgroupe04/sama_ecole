using SamaEcole.Application.Subscriptions.Queries.GetSchoolFeatures;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ce que la formule de l'école courante inclut — consommé par wwwroot/js/features.js pour masquer
/// ou marquer « Premium » les fonctionnalités hors formule.
///
/// Ouvert à TOUT utilisateur authentifié de l'école, et non au seul Directeur : le Secrétariat et la
/// Finance voient les mêmes écrans et doivent donc pouvoir les afficher correctement. C'est aussi la
/// raison d'être de ce contrôleur séparé — SubscriptionsController est restreint au Directeur au
/// niveau de la classe, et les attributs [Authorize] se cumulent (ils ne se remplacent pas).
/// </summary>
[ApiController]
[Route("api/v1/features")]
[Authorize]
public class FeaturesController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SchoolFeaturesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetFeatures(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSchoolFeaturesQuery(), cancellationToken));
}
