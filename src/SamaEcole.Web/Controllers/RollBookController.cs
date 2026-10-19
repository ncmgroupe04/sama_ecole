using SamaEcole.Application.RollBook;
using SamaEcole.Application.RollBook.Commands.RevertRollBookEntry;
using SamaEcole.Application.RollBook.Commands.UpsertRollBookEntries;
using SamaEcole.Application.RollBook.Queries.GetRollBookGrid;
using SamaEcole.Application.RollBook.Queries.GetRollBookPdf;
using SamaEcole.Application.RollBook.Queries.GetRollBookStats;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Cahier de roulement / registre d'appel bilingue (Franco-Arabe Bloc 5). Contrôleur mince : aucune logique
/// métier ici (règle #8) ; l'école vient du JWT (règle #10). Grille et saisie : les rôles qui tiennent l'appel
/// (l'Enseignant est borné à ses classes par le Handler) ; statistiques : direction et secrétariat.
/// </summary>
[ApiController]
[Route("api/v1/roll-book")]
[Authorize(Roles = "Directeur,Secretariat,Surveillant,Enseignant,SuperAdmin")]
[RequireModule(SchoolModule.Pedagogy)]
public class RollBookController(ISender mediator) : ControllerBase
{
    /// <summary>Corps du PUT : la classe est dans le corps, les cases sont un lot atomique.</summary>
    public record UpsertEntriesRequest(Guid ClassroomId, IReadOnlyList<RollBookEntryInput> Entries);

    /// <summary>Grille mensuelle d'une classe (saisies du cahier + statuts dérivés des appels).</summary>
    [HttpGet]
    [ProducesResponseType<RollBookGridDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Grid(
        [FromQuery] Guid classroomId, [FromQuery] string month, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRollBookGridQuery(classroomId, month), cancellationToken));

    /// <summary>
    /// Enregistre un lot de cases. <c>status</c> nul = revenir au statut calculé. Tout le lot passe ou rien
    /// (422 sur la première règle violée, 409 si une case a été saisie en même temps par un autre).
    /// </summary>
    [HttpPut("entries")]
    [ProducesResponseType<UpsertRollBookEntriesResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Upsert([FromBody] UpsertEntriesRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpsertRollBookEntriesCommand(request.ClassroomId, request.Entries), cancellationToken));

    /// <summary>Retire la saisie d'une case : elle retrouve le statut calculé (suppression logique).</summary>
    [HttpDelete("entries/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revert(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RevertRollBookEntryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Le cahier de roulement imprimable pour l'inspection (A4 paysage) : un mois (<c>month=AAAA-MM</c>) ou une période
    /// d'évaluation (<c>termId</c> : grilles mensuelles, récapitulatif, et synthèse du niveau pour la direction et le
    /// secrétariat). <c>lang</c> : <c>fr</c>, <c>ar</c> ou <c>bi</c> ; l'arabe et le bilingue exigent le module
    /// Franco-Arabe (422 sinon). Directeur, Secrétariat, Surveillant, Super Admin ; jamais l'Enseignant.
    /// </summary>
    [HttpGet("pdf")]
    [Authorize(Roles = "Directeur,Secretariat,Surveillant,SuperAdmin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Pdf(
        [FromQuery] Guid classroomId,
        [FromQuery] string? month,
        [FromQuery] Guid? termId,
        [FromQuery] string? lang,
        CancellationToken cancellationToken)
    {
        var file = await mediator.Send(new GetRollBookPdfQuery(classroomId, month, termId, lang), cancellationToken);
        return this.InlinePdf(file.Content, file.FileName);
    }

    /// <summary>
    /// Taux de présence, d'absence et d'assiduité d'un mois ou d'une période d'évaluation, par élève et classe
    /// (<c>scope=class</c>) ou par niveau (<c>scope=level</c>). Direction et secrétariat.
    /// </summary>
    [HttpGet("stats")]
    [Authorize(Roles = "Directeur,Secretariat,SuperAdmin")]
    [ProducesResponseType<RollBookStatsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Stats(
        [FromQuery] string scope,
        [FromQuery] Guid? classroomId,
        [FromQuery] string? month,
        [FromQuery] Guid? termId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRollBookStatsQuery(scope, classroomId, month, termId), cancellationToken));
}
