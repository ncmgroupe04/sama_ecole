using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.CreateMention;
using SamaEcole.Application.Grades.Commands.ImportGrades;
using SamaEcole.Application.Grades.Queries.GetClassGrades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Application.Grades.Commands.UpdateGrade;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Tickets JGK-G01/G02 — /grades. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// Deux endpoints distincts pour la saisie, deux permissions distinctes (docs/Volume_7_Security.md
/// « Notes ») : SAISIR une nouvelle note est réservé à l'Enseignant ; CORRIGER une note déjà saisie
/// est ouvert au Directeur ET à l'Enseignant. Un seul endpoint « upsert » aurait mélangé les deux
/// permissions.
/// </summary>
[ApiController]
[Route("api/v1/grades")]
[Authorize]
public class GradesController(ISender mediator) : ControllerBase
{
    public record UpdateGradeRequest(decimal Value, uint RowVersion);
    public record CreateMentionRequest(string Label, decimal MinAverage);

    public record ImportGradesRequest(
        Guid ClassroomId, Guid SubjectId, Guid TermId, EvaluationType EvaluationType, IFormFile? File);

    private const string GradingRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}";

    /// <summary>
    /// Lecture des mentions uniquement (docs/Volume_7_Security.md « Notes » : Voir = Directeur +
    /// Enseignant). Le Secrétariat y est ajouté à part — via JGK-G02 il peut désormais CRÉER des
    /// mentions et a donc besoin de les lire — sans toucher à GradingRoles, qui reste la permission de
    /// saisie/correction des notes (Secrétariat en est et doit en rester exclu).
    /// </summary>
    private const string MentionReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Enseignant)}";

    private const string MentionWriteRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    /// <summary>
    /// Écran de saisie des notes : les élèves d'une classe avec leurs notes déjà saisies pour une
    /// matière et un trimestre. LECTURE ouverte au Directeur et à l'Enseignant, comme la consultation
    /// des notes (Volume_7_Security « Voir »).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = GradingRoles)]
    [ProducesResponseType<IReadOnlyList<StudentGradeRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListForClass(
        [FromQuery] GetClassGradesQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpPost]
    [Authorize(Roles = nameof(Role.Enseignant))]
    [ProducesResponseType<GradeResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateGradeCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    /// <summary>
    /// Import de masse d'une colonne de notes (Devoir OU Composition) depuis un fichier CSV/Excel à
    /// deux colonnes (matricule, note) — mode de saisie alternatif à la grille cellule par cellule,
    /// même permission que la saisie unitaire (docs/Volume_7_Security.md « Notes » : Saisir = Enseignant
    /// seul). Tout le fichier est validé avant la moindre écriture (422 avec le détail ligne par ligne
    /// si une seule ligne est invalide) — voir ImportGradesCommandHandler.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = nameof(Role.Enseignant))]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<ImportGradesResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] ImportGradesRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            throw new ValidationException([new ValidationFailure("File", "Aucun fichier n'a été fourni.")]);
        }

        await using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream, cancellationToken);

        var result = await mediator.Send(new ImportGradesCommand(
            request.ClassroomId, request.SubjectId, request.TermId, request.EvaluationType,
            stream.ToArray(), request.File.FileName), cancellationToken);

        return Ok(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = GradingRoles)]
    [ProducesResponseType<GradeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateGradeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateGradeCommand(id, request.Value, request.RowVersion), cancellationToken));

    /// <summary>
    /// Ticket JGK-G02 — moyennes par matière, moyenne générale pondérée et mention. Recalculé à la
    /// demande, jamais persisté ici (la version figée du bulletin relève de JGK-G03). Lecture ouverte
    /// au Directeur et à l'Enseignant, comme la consultation des notes (Volume_7_Security « Voir »).
    /// </summary>
    [HttpGet("calculate")]
    [Authorize(Roles = GradingRoles)]
    [ProducesResponseType<GradeSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate(
        [FromQuery] Guid studentId, [FromQuery] Guid termId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetGradeSummaryQuery(studentId, termId), cancellationToken));

    /// <summary>
    /// Ticket JGK-G02 — mentions personnalisables (Volume 1 §8.4). LECTURE ouverte au Directeur, au
    /// Secrétariat et à l'Enseignant (l'écran de moyennes en a besoin) ; ÉCRITURE ouverte au Directeur
    /// et au Secrétariat (délégation en cas d'absence du Directeur — Volume_7_Security « Paramètres de
    /// l'école »), jamais à l'Enseignant.
    /// </summary>
    [HttpGet("mentions")]
    [Authorize(Roles = MentionReadRoles)]
    [ProducesResponseType<IReadOnlyList<MentionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMentions(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMentionsQuery(), cancellationToken));

    [HttpPost("mentions")]
    [Authorize(Roles = MentionWriteRoles)]
    [ProducesResponseType<MentionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateMention(
        [FromBody] CreateMentionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CreateMentionCommand(request.Label, request.MinAverage), cancellationToken);

        return CreatedAtAction(nameof(ListMentions), result);
    }
}
