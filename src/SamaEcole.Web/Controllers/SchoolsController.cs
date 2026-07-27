using SamaEcole.Application.Schools.Commands.CreateSchool;
using SamaEcole.Application.Schools.Queries.GetSchools;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-B01 — établissements. RÉSERVÉ AU SUPER ADMIN (openapi.yaml, docs/Volume_7_Security.md
/// §4 : « Créer une école » = Super Admin uniquement, Directeur exclu).
///
/// C'est le seul endroit du système où l'on voit toutes les écoles à la fois. Le Super Admin n'y
/// accède qu'aux métadonnées d'établissement : il n'a aucun claim schoolId, donc la RLS lui ferme
/// toutes les tables tenant (élèves, notes, finances) — §8.
/// </summary>
[ApiController]
[Route("api/v1/schools")]
[Authorize(Roles = nameof(Role.SuperAdmin))]
public class SchoolsController(ISender mediator) : ControllerBase
{
    public record CreateSchoolRequest(
        string Name,
        string Address,
        string? Phone,
        string DirectorEmail,
        string? DirectorFullName,
        SubscriptionPlan Plan);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SchoolSummary>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSchoolsQuery(), cancellationToken));

    /// <summary>
    /// Crée l'établissement ET son compte Directeur. La réponse ne contient JAMAIS le mot de passe
    /// généré : il ne part que par l'e-mail adressé au Directeur.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreateSchoolResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSchoolRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateSchoolCommand(
                request.Name, request.Address, request.Phone, request.DirectorEmail, request.DirectorFullName,
                request.Plan),
            cancellationToken);

        return CreatedAtAction(nameof(GetAll), new { id = result.SchoolId }, result);
    }
}
