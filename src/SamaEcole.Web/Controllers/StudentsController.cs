using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Students.Commands.CorrectStudentMatricule;
using SamaEcole.Application.Students.Commands.CreateStudent;
using SamaEcole.Application.Students.Commands.DeleteStudent;
using SamaEcole.Application.Students.Commands.ImportStudents;
using SamaEcole.Application.Students.Commands.SetStudentPhoto;
using SamaEcole.Application.Students.Commands.UpdateStudent;
using SamaEcole.Domain.Enums;
using SamaEcole.Application.Students.Queries.GetStudentDetail;
using SamaEcole.Application.Students.Queries.GetStudentImportTemplate;
using SamaEcole.Application.Students.Queries.GetStudents;
using SamaEcole.Application.Students.Queries.GetStudentsExportPdf;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Contrôleur de référence : mince, aucune logique métier, traduit HTTP ↔ MediatR
/// (AGENTS.md règle #8). Voir openapi.yaml pour le contrat complet, ticket JGK-D01.
///
/// LECTURE ouverte à tout utilisateur authentifié (l'Enseignant consulte ses classes).
/// CRÉER, CORRIGER ou ARCHIVER une fiche élève est réservé au Directeur et au Secrétariat —
/// voir docs/Volume_7_Security.md §15 (matrice Élèves).
/// </summary>
[ApiController]
[Route("api/v1/students")]
[Authorize]
public class StudentsController(ISender mediator) : ControllerBase
{
    public record SetStudentPhotoRequest(string? PhotoData, uint RowVersion);

    public record CorrectMatriculeRequest(string NewMatricule, uint RowVersion);

    public record ImportStudentsRequest(IFormFile? File, bool DryRun);

    public record UpdateStudentRequest(
        string FullName,
        DateOnly BirthDate,
        string BirthPlace,
        string Gender,
        Guid ClassroomId,
        string? PhotoUrl,
        string? GuardianName,
        string? GuardianPhone,
        string? GuardianEmail,
        string? Address,
        uint RowVersion);

    private const string ManageRoles = "Directeur,Secretariat";

    /// <summary>Export PDF (docs/Volume_7_Security.md §15, matrice Élèves) : ni Finance ni Enseignant
    /// n'administre la fiche élève, mais Finance en a besoin pour ses propres listes de recouvrement.</summary>
    private const string ExportRoles = "Directeur,Secretariat,Finance";

    /// <summary>
    /// La requête est liée depuis la chaîne de requête. Elle ne porte PAS de SchoolId : l'école est
    /// lue dans le JWT (AGENTS.md règle #10) — l'accepter du client permettrait de lire les élèves
    /// d'un autre établissement en changeant un paramètre d'URL.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PaginatedStudents>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] GetStudentsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>
    /// Fiche élève complète (ticket JGK-D02) : identité, historique scolaire, notes par trimestre et
    /// historique des paiements. Comme pour la liste, l'école vient du JWT (règle #10) : un id d'une
    /// autre école est introuvable (404), jamais servi. Les sections vides reviennent en listes vides
    /// pour laisser l'UI afficher un empty-state.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<StudentDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentDetailQuery(id), cancellationToken));

    /// <summary>
    /// Liste tabulaire des élèves en PDF, filtrable par classe et par année active — même périmètre
    /// que <see cref="List"/>, sans pagination. Réservé à Directeur/Secrétariat/Finance
    /// (docs/Volume_7_Security.md §15), à l'exclusion de l'Enseignant.
    /// </summary>
    [HttpGet("export/pdf")]
    [Authorize(Roles = ExportRoles)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] Guid? classroomId, [FromQuery] bool activeYearOnly,
        [FromQuery] bool notEnrolledForActiveYear, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetStudentsExportPdfQuery
            {
                ClassroomId = classroomId,
                ActiveYearOnly = activeYearOnly,
                NotEnrolledForActiveYear = notEnrolledForActiveYear
            },
            cancellationToken);

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"Eleves_{DateTime.UtcNow:yyyy-MM-dd}.pdf\"";
        return File(result.Content, "application/pdf");
    }

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CreateStudentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateStudentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    /// <summary>
    /// Import de masse pour la rentrée scolaire (fichier CSV/Excel, une classe par ligne) — bouton
    /// « Télécharger le modèle Excel d'exemple » de l'écran d'import. Voir GetStudentImportTemplateQuery.
    /// </summary>
    [HttpGet("import/template")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetStudentImportTemplateQuery(), cancellationToken);
        return File(
            result.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.FileName);
    }

    /// <summary>
    /// Import de masse (ticket import Excel/CSV, D-MAJ §1.C) — <c>dryRun=true</c> pour l'aperçu (rien
    /// n'est écrit, réponse 200 avec le détail ligne par ligne), <c>dryRun=false</c> pour la confirmation
    /// (422 si la moindre ligne est invalide, RIEN écrit ; sinon les élèves sont créés en une seule
    /// transaction). Voir ImportStudentsCommand pour le pourquoi de ce contrat en deux appels.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = ManageRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<ImportStudentsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] ImportStudentsRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            throw new ValidationException([new ValidationFailure("File", "Aucun fichier n'a été fourni.")]);
        }

        await using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream, cancellationToken);

        var result = await mediator.Send(
            new ImportStudentsCommand(stream.ToArray(), request.File.FileName, request.DryRun), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Corrige les informations non financières et non sécurisées d'une fiche élève déjà créée
    /// (état civil, classe, coordonnées du tuteur). Le matricule n'est jamais modifiable (règle #3).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UpdateStudentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateStudentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateStudentCommand(
                id, request.FullName, request.BirthDate, request.BirthPlace, request.Gender,
                request.ClassroomId, request.PhotoUrl, request.GuardianName, request.GuardianPhone,
                request.GuardianEmail, request.Address, request.RowVersion),
            cancellationToken));

    /// <summary>
    /// Corrige le MATRICULE d'un élève (Option 2). Endpoint DÉDIÉ et réservé au DIRECTEUR seul —
    /// contrairement au reste de la fiche, ouvert au Secrétariat : le matricule est un identifiant
    /// officiel, sa rectification n'est pas une correction de saisie ordinaire (voir
    /// CorrectStudentMatriculeCommand). 409 si le nouveau matricule est déjà pris, ou si la fiche a
    /// changé entre-temps (verrou optimiste).
    /// </summary>
    [HttpPut("{id:guid}/matricule")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<CorrectStudentMatriculeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CorrectMatricule(
        Guid id, [FromBody] CorrectMatriculeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new CorrectStudentMatriculeCommand(id, request.NewMatricule, request.RowVersion), cancellationToken));

    /// <summary>
    /// Feature B — dépose, remplace ou retire (PhotoData null) la photo téléversée d'une fiche déjà
    /// créée. Commande DÉDIÉE, distincte de Update : voir SetStudentPhotoCommand pour le pourquoi.
    /// </summary>
    [HttpPut("{id:guid}/photo")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SetStudentPhotoResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetPhoto(
        Guid id, [FromBody] SetStudentPhotoRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new SetStudentPhotoCommand(id, request.PhotoData, request.RowVersion), cancellationToken));

    /// <summary>
    /// Archive (soft delete) une fiche élève créée par pure erreur de saisie. Refusée en 409 dès
    /// qu'une inscription ou une note existe déjà (DeleteStudentCommandHandler). `rowVersion` en
    /// query string, comme DELETE /grades/{id}.
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
        await mediator.Send(new DeleteStudentCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
