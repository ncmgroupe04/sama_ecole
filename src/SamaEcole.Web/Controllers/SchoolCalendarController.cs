using SamaEcole.Application.SchoolCalendar;
using SamaEcole.Application.SchoolCalendar.Commands.CreateSchoolClosure;
using SamaEcole.Application.SchoolCalendar.Commands.DeleteSchoolClosure;
using SamaEcole.Application.SchoolCalendar.Commands.UpdateSchoolClosure;
using SamaEcole.Application.SchoolCalendar.Queries.ListSchoolClosures;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Calendrier de fermeture (jours fériés, vacances) — Franco-Arabe Bloc 5. Contrôleur mince : aucune
/// logique métier ici (règle #8) ; l'école vient du JWT (règle #10). Lecture ouverte aux rôles qui
/// consultent ou saisissent le cahier de roulement ; écriture réservée au Directeur, comme l'année
/// scolaire (docs/Volume_7_Security.md §15).
/// </summary>
[ApiController]
[Route("api/v1/school-calendar/closures")]
[Authorize(Roles = "Directeur,Secretariat,Enseignant,Surveillant,SuperAdmin")]
[RequireModule(SchoolModule.Pedagogy)]
public class SchoolCalendarController(ISender mediator) : ControllerBase
{
    /// <summary>Corps du POST/PUT : la ressource visée vient de la route, jamais d'un champ du corps.</summary>
    public record SaveClosureRequest(
        DateOnly StartDate,
        DateOnly EndDate,
        SchoolClosureKind Kind,
        string Label,
        string? LabelAr = null);

    /// <summary>Fermetures d'une année (par défaut l'année active), dans l'ordre du calendrier.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SchoolClosureDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid? schoolYearId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListSchoolClosuresQuery(schoolYearId), cancellationToken));

    [HttpPost]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolClosureDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromQuery] Guid schoolYearId, [FromBody] SaveClosureRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateSchoolClosureCommand(
                schoolYearId, request.StartDate, request.EndDate, request.Kind, request.Label, request.LabelAr),
            cancellationToken);

        return CreatedAtAction(nameof(List), new { schoolYearId = result.SchoolYearId }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<SchoolClosureDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] SaveClosureRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateSchoolClosureCommand
            {
                Id = id,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                Kind = request.Kind,
                Label = request.Label,
                LabelAr = request.LabelAr
            },
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteSchoolClosureCommand(id), cancellationToken);
        return NoContent();
    }
}
