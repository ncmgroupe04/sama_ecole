using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Coefficients.Queries;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Coefficients par série et surcharge du Directeur (Évolution N°4). Contrôleur mince : aucune logique
/// métier ici (règle #8). L'école vient du JWT, l'année de l'année ACTIVE résolue serveur (règle #10).
///
/// LECTURE : Directeur et Secrétariat. ÉCRITURE : Directeur SEUL, sans délégation (arbitrage A9) — un
/// coefficient pilote toutes les moyennes de l'établissement.
/// </summary>
[ApiController]
[Route("api/v1/coefficients")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class CoefficientsController(ISender mediator) : ControllerBase
{
    private const string ReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";
    private const string WriteRole = nameof(Role.Directeur);

    /// <summary>Catalogue fermé des séries de lycée.</summary>
    [HttpGet("catalog")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<SeriesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSeriesCatalogQuery(), cancellationToken));

    /// <summary>Grille des coefficients d'une série OU d'une classe pour une année (défaut : l'année active).</summary>
    [HttpGet]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<CoefficientGridDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Grid(
        [FromQuery] string? series, [FromQuery] Guid? classroomId, [FromQuery] Guid? schoolYearId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCoefficientGridQuery(series, classroomId, schoolYearId), cancellationToken));

    /// <summary>Pose ou corrige une surcharge (année active). 409 si la ligne a changé entre-temps.</summary>
    [HttpPut]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType<CoefficientOverrideResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertCoefficientOverrideCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>« Rétablir » : supprime (logiquement) la surcharge. `rowVersion` en query string, comme DELETE /subjects.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteCoefficientOverrideCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// « Appliquer le modèle » d'une série : crée les surcharges de série de l'année active à partir du modèle
    /// national. Ne touche jamais les coefficients des matières ; 422 pour une série sans modèle (TECH).
    /// </summary>
    [HttpPost("apply-template")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType<ApplyTemplateResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyTemplate(
        [FromBody] ApplySeriesTemplateCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Recopie les surcharges d'une autre année vers l'année active, sans rien écraser.</summary>
    [HttpPost("carry-over")]
    [Authorize(Roles = WriteRole)]
    [ProducesResponseType<CarryOverResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CarryOver(
        [FromBody] CarryOverCoefficientsCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));
}
