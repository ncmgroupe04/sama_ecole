using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Commands.CreateGrade;
using SamaEcole.Application.Grades.Commands.CreateMention;
using SamaEcole.Application.Grades.Commands.UpdateMention;
using SamaEcole.Application.Grades.Commands.DeleteMention;
using SamaEcole.Application.Grades.Commands.DeleteGrade;
using SamaEcole.Application.Grades.Commands.ImportGradeSheet;
using SamaEcole.Application.Grades.Queries.GetClassGrades;
using SamaEcole.Application.Grades.Queries.GetGradeSheetExcel;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Application.Grades.Commands.UpdateGrade;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Tickets JGK-G01/G02 — /grades. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// SAISIR une nouvelle note est ouvert au Directeur et à l'Enseignant (Volume_7_Security.md §14,
/// table « Notes »). CORRIGER ou ANNULER une note déjà saisie en base est réservé au Directeur et au
/// Secrétariat — matrice d'autorisation "Photoshop", contrôle strict et NON révocable (contrairement
/// à la délégation du barème/matières/mentions, qui repose sur SchoolSettings.AllowSecretaryToManageGrading) :
/// l'Enseignant ne peut plus jamais modifier une note une fois enregistrée, une erreur de saisie se
/// corrige exclusivement via ces deux rôles. Un seul endpoint « upsert » aurait mélangé ces permissions
/// désormais distinctes.
/// </summary>
[ApiController]
[Route("api/v1/grades")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class GradesController(ISender mediator) : ControllerBase
{
    public record UpdateGradeRequest(decimal Value, uint RowVersion);
    public record CreateMentionRequest(string Label, decimal MinAverage);

    public record ImportGradeSheetRequest(
        Guid ClassroomId, Guid SubjectId, Guid TermId, bool DryRun, IFormFile? File);

    /// <summary>SAISIR une note (POST) et le calcul des moyennes (GET calculate) : inchangés par la matrice "Photoshop".</summary>
    private const string GradingRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}";

    /// <summary>
    /// Écran de saisie/correction : LECTURE ouverte au Directeur, au Secrétariat (qui peut désormais
    /// corriger/annuler) et à l'Enseignant (qui saisit). Distincte de GradingRoles, qui reste la
    /// permission d'ÉCRITURE de la saisie initiale (Enseignant seul).
    /// </summary>
    private const string ViewGradesRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Enseignant)}";

    /// <summary>
    /// CORRIGER ou ANNULER une note déjà enregistrée (PUT, DELETE) : Directeur et Secrétariat
    /// uniquement, JAMAIS l'Enseignant — même l'auteur de la saisie initiale. Contrôle strict et non
    /// révocable (matrice d'autorisation "Photoshop"), pas une délégation optionnelle.
    /// </summary>
    private const string UpdateGradeRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    /// <summary>
    /// Lecture des mentions uniquement (docs/Volume_7_Security.md « Notes » : Voir = Directeur +
    /// Enseignant). Le Secrétariat y est ajouté à part, SANS condition sur la délégation
    /// (contrairement à l'écriture, voir GradingPolicies.CanManageGradingScale) : il en a besoin pour
    /// composer les bulletins, que la gestion des mentions lui soit déléguée ou non.
    /// </summary>
    private const string MentionReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Enseignant)}";

    /// <summary>
    /// Écran de saisie des notes : les élèves d'une classe avec leurs notes déjà saisies pour une
    /// matière et un trimestre. LECTURE ouverte au Directeur, au Secrétariat et à l'Enseignant — le
    /// Secrétariat en a besoin pour corriger/annuler une note déjà saisie (voir UpdateGradeRoles).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = ViewGradesRoles)]
    [ProducesResponseType<IReadOnlyList<StudentGradeRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListForClass(
        [FromQuery] GetClassGradesQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpPost]
    [Authorize(Roles = GradingRoles)]
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
    /// Génère la feuille de notes Excel (Matricule, Nom &amp; Prénom, Devoir 1, Devoir 2, Composition)
    /// d'une classe/matière/trimestre, pré-remplie avec les notes déjà saisies — bouton « Télécharger la
    /// feuille » de la modale d'import. Même permission de lecture que la grille de saisie.
    /// </summary>
    [HttpGet("sheet/export")]
    [Authorize(Roles = ViewGradesRoles)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportSheet(
        [FromQuery] Guid classroomId, [FromQuery] Guid subjectId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetGradeSheetExcelQuery(classroomId, subjectId, termId), cancellationToken);

        return File(
            result.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.FileName);
    }

    /// <summary>
    /// Import de masse des trois épreuves (Devoir 1, Devoir 2, Composition) depuis un fichier Excel au
    /// format large (une ligne par élève, une colonne par épreuve, reconnue par le NOM de son en-tête) —
    /// mode de saisie alternatif à la grille cellule par cellule, même permission que la saisie unitaire
    /// (Saisir = Directeur ou Enseignant). <c>dryRun=true</c> valide tout le fichier sans rien écrire
    /// (aperçu) ; <c>dryRun=false</c> re-valide puis écrit dans une seule transaction si tout est valide
    /// (422 avec le détail ligne par ligne sinon) — voir ImportGradeSheetCommandHandler.
    /// </summary>
    [HttpPost("sheet/import")]
    [Authorize(Roles = GradingRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<ImportGradeSheetResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ImportSheet([FromForm] ImportGradeSheetRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            throw new ValidationException([new ValidationFailure("File", "Aucun fichier n'a été fourni.")]);
        }

        await using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream, cancellationToken);

        var result = await mediator.Send(new ImportGradeSheetCommand(
            request.ClassroomId, request.SubjectId, request.TermId, request.DryRun,
            stream.ToArray(), request.File.FileName), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Corrige une note déjà saisie. Réservé au Directeur et au Secrétariat — l'Enseignant, même
    /// auteur de la saisie initiale, ne peut plus la modifier une fois enregistrée (contrôle strict,
    /// matrice d'autorisation "Photoshop").
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = UpdateGradeRoles)]
    [ProducesResponseType<GradeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateGradeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateGradeCommand(id, request.Value, request.RowVersion), cancellationToken));

    /// <summary>
    /// Annule (soft delete) une note déjà saisie. Même permission que la correction : Directeur et
    /// Secrétariat uniquement, jamais l'Enseignant. `rowVersion` en query string, comme
    /// DELETE /finance/fees/{id} : une suppression n'a pas de corps de requête à transporter.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = UpdateGradeRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteGradeCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

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
    /// Secrétariat et à l'Enseignant (l'écran de moyennes en a besoin) ; ÉCRITURE : Directeur toujours,
    /// Secrétariat seulement si SON école a activé la délégation (GradingPolicies.CanManageGradingScale),
    /// jamais l'Enseignant.
    /// </summary>
    [HttpGet("mentions")]
    [Authorize(Roles = MentionReadRoles)]
    [ProducesResponseType<IReadOnlyList<MentionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMentions(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMentionsQuery(), cancellationToken));

    [HttpPost("mentions")]
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
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

    public record UpdateMentionRequest(string Label, decimal MinAverage);

    /// <summary>Corrige le libellé/seuil d'une mention en cas d'erreur de saisie — sans repasser par une suppression puis recréation de toute la liste.</summary>
    [HttpPatch("mentions/{id:guid}")]
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
    [ProducesResponseType<MentionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateMention(
        Guid id, [FromBody] UpdateMentionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new UpdateMentionCommand(id, request.Label, request.MinAverage), cancellationToken);

        return Ok(result);
    }

    /// <summary>Suppression logique (AGENTS.md règle #6) — jamais un effacement physique.</summary>
    [HttpDelete("mentions/{id:guid}")]
    [Authorize(Policy = GradingPolicies.CanManageGradingScale)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMention(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteMentionCommand(id), cancellationToken);
        return NoContent();
    }
}
