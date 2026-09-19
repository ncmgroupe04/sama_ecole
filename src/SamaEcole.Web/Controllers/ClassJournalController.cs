using SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;
using SamaEcole.Application.ClassJournal.Commands.DeleteClassJournalEntry;
using SamaEcole.Application.ClassJournal.Commands.UpdateClassJournalEntry;
using SamaEcole.Application.ClassJournal.Queries.GetClassJournal;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-P04 — Cahier de texte / Journal de classe (openapi.yaml). Contrôleur mince, aucune
/// logique métier (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient
/// du JWT (règle #10).
///
/// LECTURE ouverte à Directeur/Secrétariat/Surveillant/Enseignant (document pédagogique partagé,
/// pas un carnet privé). ÉCRITURE : la CRÉATION est réservée à l'Enseignant titulaire du créneau
/// (garde fine dans ClassJournalScopeAuthorizer, 409 si aucun créneau planifié) ; la CORRECTION
/// (modification/suppression) est ouverte à Enseignant/Directeur/Secrétariat au niveau du rôle,
/// mais bornée dans le Handler à « l'auteur dans les 15 jours, ou un rôle élevé au-delà »
/// (ClassJournalEditWindow, 403 sinon) — même motif que Grades (canCorrectGrades).
/// </summary>
[ApiController]
[Route("api/v1/class-journal")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class ClassJournalController(ISender mediator) : ControllerBase
{
    public record CreateEntryRequest(
        Guid ClassroomId, Guid SubjectId, DateOnly SessionDate, string Topic, string Content,
        string? Homework, DateOnly? HomeworkDueDate);

    public record UpdateEntryRequest(
        string Topic, string Content, string? Homework, DateOnly? HomeworkDueDate, uint RowVersion);

    private const string ViewRoles =
        $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Surveillant)},{nameof(Role.Enseignant)}";

    private const string CorrectRoles =
        $"{nameof(Role.Enseignant)},{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    [HttpGet]
    [Authorize(Roles = ViewRoles)]
    [ProducesResponseType<PaginatedClassJournalEntries>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] GetClassJournalQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>Réservé à l'Enseignant : lui seul journalise une séance, et seulement la sienne (409 sinon).</summary>
    [HttpPost]
    [Authorize(Roles = nameof(Role.Enseignant))]
    [ProducesResponseType<ClassJournalEntryResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateEntryRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateClassJournalEntryCommand
            {
                ClassroomId = request.ClassroomId,
                SubjectId = request.SubjectId,
                SessionDate = request.SessionDate,
                Topic = request.Topic,
                Content = request.Content,
                Homework = request.Homework,
                HomeworkDueDate = request.HomeworkDueDate
            },
            cancellationToken);

        return CreatedAtAction(nameof(List), new { id = result.Id }, result);
    }

    /// <summary>Corrige le sujet/contenu/devoirs d'une entrée — jamais la classe, la matière ni la date
    /// de séance (voir UpdateClassJournalEntryCommand). Garde fine des 15 jours dans le Handler.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = CorrectRoles)]
    [ProducesResponseType<ClassJournalEntryResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateEntryRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateClassJournalEntryCommand
            {
                Id = id,
                Topic = request.Topic,
                Content = request.Content,
                Homework = request.Homework,
                HomeworkDueDate = request.HomeworkDueDate,
                RowVersion = request.RowVersion
            },
            cancellationToken));

    /// <summary>Archive (soft delete) une entrée — même garde des 15 jours que Update. `rowVersion` en
    /// query string, comme DELETE /students/{id}.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = CorrectRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteClassJournalEntryCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }
}
