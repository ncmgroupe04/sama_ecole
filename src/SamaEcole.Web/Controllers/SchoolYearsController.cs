using SamaEcole.Application.SchoolYears;
using SamaEcole.Application.SchoolYears.Commands.ActivateSchoolYear;
using SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;
using SamaEcole.Application.SchoolYears.Commands.UpdateSchoolYear;
using SamaEcole.Application.SchoolYears.Queries.ExportSchoolYear;
using SamaEcole.Application.SchoolYears.Queries.GetSchoolYears;
using SamaEcole.Application.SchoolYears.Queries.GetTerms;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-C01 — /school-years (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
/// </summary>
[ApiController]
[Route("api/v1/school-years")]
[Authorize]
public class SchoolYearsController(ISender mediator) : ControllerBase
{
    /// <summary>Corps du POST d'activation : la ressource est dans l'URL, le mot de passe confirme l'acte.</summary>
    public record ActivateRequest(string Password);

    /// <summary>
    /// LECTURE ouverte à tout utilisateur de l'école : l'année active est le contexte de travail de
    /// tous les écrans (inscriptions, frais, notes). La réserver au Directeur obligerait chaque autre
    /// rôle à travailler sans savoir sur quel exercice il écrit.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SchoolYearDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSchoolYearsQuery(), cancellationToken));

    /// <summary>
    /// ÉCRITURE réservée au Directeur : l'année scolaire est un paramètre d'établissement
    /// (docs/Volume_7_Security.md §15 — « Informations, année scolaire, notation… : Directeur »).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolYearDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSchoolYearCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    /// <summary>
    /// Corrige le libellé et/ou la période d'une année (ticket JGK-C01) — typiquement prolonger une
    /// année de quelques semaines quand le calendrier scolaire se décale. Directeur, comme la création :
    /// l'année scolaire est un paramètre d'établissement (docs/Volume_7_Security.md §15).
    ///
    /// PAS de ressaisie du mot de passe, contrairement à l'activation : celle-ci change l'exercice sur
    /// lequel s'imputent les écritures du jour (§16), là où corriger des dates ne déplace aucune donnée
    /// d'un exercice à l'autre. Les trimestres, eux, sont recalés — voir UpdateSchoolYearCommandHandler.
    ///
    /// L'id vient de la ROUTE et écrase celui du corps : les deux ne doivent pas pouvoir diverger.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolYearDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateSchoolYearCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command with { Id = id }, cancellationToken));

    /// <summary>
    /// Bascule de l'année active — Directeur, avec ressaisie du mot de passe
    /// (docs/Volume_7_Security.md §16 : « changement de l'année scolaire active »).
    ///
    /// POST et non PUT : ce n'est pas le remplacement d'une représentation, c'est une action métier
    /// qui en modifie DEUX ressources — l'année qui s'active, et celle qui se désactive.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolYearDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Activate(
        Guid id, [FromBody] ActivateRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivateSchoolYearCommand(id, request.Password), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Trimestres générés automatiquement à la création de l'année (ticket JGK-G01) — LECTURE ouverte
    /// à tout utilisateur de l'école, comme la liste des années : l'écran de saisie de notes en a
    /// besoin, quel que soit le rôle.
    /// </summary>
    [HttpGet("{id:guid}/terms")]
    [ProducesResponseType<IReadOnlyList<TermDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Terms(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTermsQuery(id), cancellationToken));

    /// <summary>
    /// Export ZIP (élèves, paiements, classes) d'une année scolaire — pour archivage hors plateforme.
    /// Réservé au Directeur : croise des données personnelles (élèves) et financières (paiements) de
    /// toute l'école, un périmètre plus large que celui d'un seul rôle métier.
    /// </summary>
    [HttpGet("{id:guid}/export")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ExportSchoolYearQuery(id), cancellationToken);

        return File(result.Content, "application/zip", result.FileName);
    }
}
