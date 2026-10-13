using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Écoles dont l'effectif dépasse le plafond de leur tranche — de quoi relancer proactivement leur Directeur. RÉSERVÉ AU SUPER
/// ADMIN. L'état est relu d'après les élèves vivants de chaque école ; <c>overQuotaSince</c> n'en donne que la date de premier
/// constat (null tant qu'aucune évaluation du quota n'a eu lieu depuis le dépassement).
/// </summary>
[ApiController]
[Route("api/v1/admin/over-quota-schools")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class AdminOverQuotaSchoolsController(ISender mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OverQuotaSchoolDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetOverQuotaSchoolsQuery(), cancellationToken));
}
