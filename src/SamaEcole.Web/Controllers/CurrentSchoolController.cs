using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;
using SamaEcole.Application.Schools.Queries.GetCurrentSchool;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Identité de l'établissement COURANT (openapi.yaml /schools/current).
///
/// À NE PAS confondre avec <see cref="SchoolsController"/> (api/v1/schools), réservé au Super Admin
/// pour créer et lister les écoles : ici c'est le DIRECTEUR qui entretient SA fiche établissement —
/// nom, coordonnées, logo — reprise sur le reçu d'inscription. « current » vient du claim JWT, jamais
/// d'un paramètre de route (AGENTS.md règle #10).
/// </summary>
[ApiController]
[Route("api/v1/schools/current")]
[Authorize]
public class CurrentSchoolController(ISender mediator) : ControllerBase
{
    public record UpdateSchoolProfileRequest(
        string Name,
        string? Address,
        string? Phone,
        string? LogoUrl,
        string? InspectionAcademie,
        string? InspectionEducationFormation,
        string? NomLycee);

    /// <summary>
    /// LECTURE ouverte à tout utilisateur de l'école : le nom et les coordonnées s'affichent sur le
    /// reçu et les écrans, quel que soit le rôle.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<SchoolProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCurrentSchoolQuery(), cancellationToken));

    /// <summary>ÉCRITURE réservée au Directeur (docs/Volume_7_Security.md §15 : configuration de l'établissement).</summary>
    [HttpPut]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateSchoolProfileRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateCurrentSchoolCommand(
                request.Name, request.Address, request.Phone, request.LogoUrl,
                request.InspectionAcademie, request.InspectionEducationFormation, request.NomLycee),
            cancellationToken));
}
