using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Classrooms.Commands.DeleteClassroom;
using SamaEcole.Application.Classrooms.Commands.UpdateClassroom;
using SamaEcole.Application.Classrooms.Queries.GetClassrooms;
using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;
using SamaEcole.Application.SchoolYears.Queries.GetSchoolYears;
using SamaEcole.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-C02 — /classrooms (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// LECTURE ouverte à tout utilisateur authentifié (l'Enseignant consulte l'arborescence des classes).
/// CRÉER, CORRIGER ou ARCHIVER une classe est réservé au Directeur et au Secrétariat — ce sont ces
/// deux rôles qui gèrent l'organisation des classes au quotidien (docs/Volume_7_Security.md §15).
/// </summary>
[ApiController]
[Route("api/v1/classrooms")]
[Authorize]
public class ClassroomsController(ISender mediator) : ControllerBase
{
    /// <summary>
    /// <paramref name="IsAccelerated"/> / <paramref name="TargetLevel"/> : classe passerelle validant
    /// DEUX niveaux (option). Absents du corps de requête d'un client existant → false/null, soit
    /// exactement le comportement d'avant l'option.
    /// </summary>
    public record UpdateClassroomRequest(
        string Name, string Level, int Capacity, uint RowVersion,
        bool IsAccelerated = false, string? TargetLevel = null, string? Series = null);

    private const string ManageRoles = "Directeur,Secretariat";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ClassroomDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassroomsQuery(), cancellationToken));

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CreateClassroomResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateClassroomCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    /// <summary>Corrige le libellé/niveau/capacité d'une classe déjà créée (erreur de saisie).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<ClassroomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateClassroomRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateClassroomCommand(
                id, request.Name, request.Level, request.Capacity, request.RowVersion,
                request.IsAccelerated, request.TargetLevel, request.Series),
            cancellationToken));

    /// <summary>
    /// Archive (soft delete) une classe créée par erreur. Refusée en 409 si des élèves y sont encore
    /// rattachés (DeleteClassroomCommandHandler). `rowVersion` en query string, comme DELETE /grades/{id}.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteClassroomCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Génère et télécharge le PDF contenant les cartes scolaires (avec QR Code)
    /// pour tous les élèves d'une classe pour l'année scolaire donnée.
    /// </summary>
    [HttpGet("{id:guid}/school-cards")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DownloadSchoolCards(
        Guid id, [FromQuery] Guid? schoolYearId, CancellationToken cancellationToken)
    {
        // Si non fourni, on prend l'année active. `await` direct plutôt que ContinueWith(t => t.Result) :
        // t.Result réemballe toute exception du Handler dans une AggregateException, que
        // ExceptionHandlingMiddleware ne sait pas traduire (un NOT_FOUND ou un 422 deviendrait un 500) ;
        // et ContinueWith sans TaskScheduler explicite hérite d'un ordonnanceur ambigu.
        var targetYearId = schoolYearId ?? Guid.Empty;
        if (targetYearId == Guid.Empty)
        {
            var schoolYears = await mediator.Send(new GetSchoolYearsQuery(), cancellationToken);
            targetYearId = schoolYears.FirstOrDefault(y => y.IsActive)?.Id ?? Guid.Empty;
        }

        var pdfBytes = await mediator.Send(new GetSchoolCardsPdfQuery(id, targetYearId), cancellationToken);

        // `inline` : le PDF s'ouvre d'abord dans la modale d'aperçu partagée (_PdfPreviewModal),
        // jamais un téléchargement forcé.
        return this.InlinePdf(pdfBytes, $"Cartes_Scolaires_{id}.pdf");
    }
}
