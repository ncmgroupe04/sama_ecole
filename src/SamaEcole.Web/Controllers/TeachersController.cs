using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Teachers.Commands.AssignTeacher;
using SamaEcole.Application.Teachers.Commands.CreateTeacher;
using SamaEcole.Application.Teachers.Commands.DeleteTeacher;
using SamaEcole.Application.Teachers.Commands.ImportTeachers;
using SamaEcole.Application.Teachers.Commands.SetTeacherPhoto;
using SamaEcole.Application.Teachers.Commands.UpdateTeacher;
using SamaEcole.Application.Teachers.Queries.GetTeacherById;
using SamaEcole.Application.Teachers.Queries.GetTeacherImportTemplate;
using SamaEcole.Application.Teachers.Queries.GetTeachers;
using SamaEcole.Application.Teachers.Queries.GetTeachersExportPdf;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-D03 — /teachers (openapi.yaml). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// Permissions docs/Volume_7_Security.md « Enseignants » : Voir → Super Admin/Directeur/Secrétariat ;
/// Créer/Modifier → Directeur/Secrétariat uniquement (contrairement aux Élèves, Finance et Enseignant
/// n'ont ici aucun accès, même en lecture).
/// </summary>
[ApiController]
[Route("api/v1/teachers")]
[Authorize]
public class TeachersController(ISender mediator) : ControllerBase
{
    public record AssignTeacherRequest(Guid ClassroomId, Guid SubjectId);

    public record ImportTeachersRequest(IFormFile? File, bool DryRun);

    public record SetTeacherPhotoRequest(string? PhotoData, uint RowVersion);

    public record UpdateTeacherRequest(
        string FullName,
        string Email,
        string? Phone,
        DateOnly BirthDate,
        string? BirthPlace,
        string? Address,
        string? PhotoUrl,
        IReadOnlyList<Guid> SubjectIds,
        uint RowVersion);

    private const string ViewRoles =
        $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    private const string ManageRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    [HttpGet]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType<PaginatedTeachers>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] GetTeachersQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>
    /// « LISTE DES ENSEIGNANTS » en PDF — pendant de <c>StudentsController.ExportPdf</c>, même
    /// périmètre que <see cref="List"/> sans pagination. Réservé aux mêmes rôles que la consultation
    /// (docs/Volume_7_Security.md « Enseignants ») : un export n'ouvre aucune donnée que le rôle ne
    /// puisse déjà lire à l'écran.
    /// </summary>
    [HttpGet("export/pdf")]
    [Authorize(Roles = ViewRoles)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportPdf([FromQuery] EntityStatus? status, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTeachersExportPdfQuery { Status = status }, cancellationToken);

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"Enseignants_{DateTime.UtcNow:yyyy-MM-dd}.pdf\"";
        return File(result.Content, "application/pdf");
    }

    [HttpPost]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<CreateTeacherResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateTeacherCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }

    /// <summary>
    /// Import de masse du corps professoral (fichier CSV/Excel, un enseignant par ligne) — bouton
    /// « Télécharger le modèle Excel d'exemple » de l'écran d'import. Même contrat que
    /// StudentsController.DownloadImportTemplate.
    /// </summary>
    [HttpGet("import/template")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTeacherImportTemplateQuery(), cancellationToken);
        return File(
            result.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            result.FileName);
    }

    /// <summary>
    /// Import de masse (<c>dryRun=true</c> pour l'aperçu, rien n'est écrit ; <c>dryRun=false</c> pour la
    /// confirmation, 422 si la moindre ligne est invalide, RIEN écrit ; sinon les enseignants sont créés
    /// en une seule transaction). Voir ImportTeachersCommand pour le pourquoi de ce contrat en deux
    /// appels — même contrat que StudentsController.Import.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Roles = ManageRoles)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [ProducesResponseType<ImportTeachersResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] ImportTeachersRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            throw new ValidationException([new ValidationFailure("File", "Aucun fichier n'a été fourni.")]);
        }

        await using var stream = new MemoryStream();
        await request.File.CopyToAsync(stream, cancellationToken);

        var result = await mediator.Send(
            new ImportTeachersCommand(stream.ToArray(), request.File.FileName, request.DryRun), cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Ticket JGK-D04 — fiche complète : matières qualifiées, attributions classe/matière groupées
    /// par année scolaire (l'historique).
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType<TeacherProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTeacherByIdQuery(id), cancellationToken));

    /// <summary>
    /// Attribution classe/matière sur l'année ACTIVE (ticket JGK-D04) — mêmes permissions que la
    /// création de la fiche (Volume_7_Security.md « Enseignants » : Créer/Modifier).
    /// </summary>
    [HttpPost("{id:guid}/assignments")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<AssignTeacherResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Assign(
        Guid id, [FromBody] AssignTeacherRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new AssignTeacherCommand { TeacherId = id, ClassroomId = request.ClassroomId, SubjectId = request.SubjectId },
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id }, result);
    }

    /// <summary>
    /// Corrige les informations non financières et non sécurisées d'une fiche enseignant déjà créée
    /// (état civil, contact, matières qualifiées). Le matricule et le rattachement d'un compte de
    /// connexion (UserId) ne sont jamais modifiables ici (voir UpdateTeacherCommand).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<UpdateTeacherResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateTeacherRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateTeacherCommand(
                id, request.FullName, request.Email, request.Phone, request.BirthDate, request.BirthPlace,
                request.Address, request.PhotoUrl, request.SubjectIds, request.RowVersion),
            cancellationToken));

    /// <summary>
    /// Feature B — dépose, remplace ou retire (PhotoData null) la photo téléversée d'une fiche déjà
    /// créée. Commande DÉDIÉE, distincte de Update : même raisonnement que StudentsController.SetPhoto.
    /// </summary>
    [HttpPut("{id:guid}/photo")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<SetTeacherPhotoResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetPhoto(
        Guid id, [FromBody] SetTeacherPhotoRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new SetTeacherPhotoCommand(id, request.PhotoData, request.RowVersion), cancellationToken));

    /// <summary>
    /// Archive (soft delete) une fiche enseignant créée par pure erreur de saisie. Refusée en 409 dès
    /// qu'une attribution classe/matière/année existe déjà (DeleteTeacherCommandHandler). `rowVersion`
    /// en query string, comme DELETE /grades/{id}.
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
        await mediator.Send(new DeleteTeacherCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
