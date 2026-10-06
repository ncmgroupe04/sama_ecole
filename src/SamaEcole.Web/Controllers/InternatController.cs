using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Commands.AssignInstructor;
using SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;
using SamaEcole.Application.Internat.Commands.CreateInstructor;
using SamaEcole.Application.Internat.Commands.UpdateInstructor;
using SamaEcole.Application.Internat.Commands.UpdateHizbProgress;
using SamaEcole.Application.Internat.Queries.GetInstructorStudents;
using SamaEcole.Application.Internat.Queries.GetInternatDashboard;
using SamaEcole.Application.Internat.Queries.GetMyHalqa;
using SamaEcole.Application.Internat.Queries.GetStudentHizbProgress;
using SamaEcole.Application.Internat.Queries.ListInstructors;
using SamaEcole.Application.Internat.Queries.SearchBoardableStudents;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Internat (spec docs/superpowers/specs/2026-09-18-module-internat-design.md). Contrôleur
/// mince, aucune logique métier ici (AGENTS.md règle #8). Verrouillé par [RequireModule] — un
/// Directeur qui n'a pas activé l'Internat reçoit 403 MODULE_DISABLED sur toutes les routes
/// ci-dessous, y compris la lecture (spec §4 : le module est désactivé par défaut, contrairement à
/// Pédagogie/Finance).
/// </summary>
[ApiController]
[Route("api/v1/internat")]
[Authorize]
[RequireModule(SchoolModule.Internat)]
public class InternatController(ISender mediator) : ControllerBase
{
    // Directeur + Secretariat + Surveillant — décision actée en brainstorming (spec §4), même trio
    // que ParentSummonsController pour la Vie Scolaire.
    private const string ManageRoles = "Directeur,Secretariat,Surveillant";

    // Halqa et suivi par Hizb. L'Oustaz se connecte avec le rôle Enseignant ; sa portée (SA Halqa seulement) est
    // bornée par HalqaScopeAuthorizer dans les Handlers — le rôle seul ne suffit pas.
    private const string HalqaReadRoles = "Directeur,Secretariat,Surveillant,Enseignant";
    private const string HizbWriteRoles = "Directeur,Enseignant";
    private const string AssignRoles = "Directeur";

    // Gestion des fiches d'Oustaz : écriture réservée au Directeur ; la liste sert aussi à la Direction élargie
    // (Secrétariat, Surveillant) qui doit reconnaître l'Oustaz d'un élève.
    private const string InstructorWriteRoles = "Directeur";

    [HttpGet("dashboard")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<InternatDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInternatDashboardQuery(), cancellationToken));

    [HttpGet("students/search")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<BoardableStudentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchStudents([FromQuery] string term, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SearchBoardableStudentsQuery(term), cancellationToken));

    public record ChangeBoardingAssignmentRequest(Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion);

    [HttpPost("assignments/{enrollmentId:guid}")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<EnrollmentBoardingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeAssignment(
        Guid enrollmentId, [FromBody] ChangeBoardingAssignmentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ChangeBoardingAssignmentCommand(
                enrollmentId, request.RoomId, request.BoardingStatus, request.IncludeBoardingFee, request.RowVersion),
            cancellationToken));

    // ---------------------------------------------------------------- Gestion des Oustaz

    public record CreateInstructorRequest(string FullName, string? FullNameAr, string? Phone, Guid? UserId);

    /// <summary>Fiche ENTIÈRE : <c>userId</c> null détache le compte. <c>rowVersion</c> = jeton lu avec la fiche.</summary>
    public record UpdateInstructorRequest(
        string FullName, string? FullNameAr, string? Phone, Guid? UserId, EntityStatus Status, uint RowVersion);

    /// <summary>Les Oustaz de l'école (tous statuts) avec l'effectif de leur Halqa.</summary>
    [HttpGet("instructors")]
    [Authorize(Roles = ManageRoles)]
    [ProducesResponseType<IReadOnlyList<InstructorDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListInstructors(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListInstructorsQuery(), cancellationToken));

    /// <summary>Crée un Oustaz, avec en option le compte (rôle Enseignant) qui lui ouvre sa Halqa. Directeur uniquement.</summary>
    [HttpPost("instructors")]
    [Authorize(Roles = InstructorWriteRoles)]
    [ProducesResponseType<InstructorDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateInstructor(
        [FromBody] CreateInstructorRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateInstructorCommand(request.FullName, request.FullNameAr, request.Phone, request.UserId),
            cancellationToken);

        return CreatedAtAction(nameof(ListInstructors), null, result);
    }

    /// <summary>Modifie un Oustaz : identité, statut (suspension), compte lié. Directeur uniquement.</summary>
    [HttpPut("instructors/{id:guid}")]
    [Authorize(Roles = InstructorWriteRoles)]
    [ProducesResponseType<InstructorDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateInstructor(
        Guid id, [FromBody] UpdateInstructorRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateInstructorCommand(
                id, request.FullName, request.FullNameAr, request.Phone, request.UserId, request.Status, request.RowVersion),
            cancellationToken));

    // ---------------------------------------------------------------- Halqa et suivi coranique par Hizb

    public record AssignInstructorRequest(Guid? InstructorId, List<Guid> StudentIds);

    public record UpdateHizbProgressRequest(int HizbNumber, int CompletedQuarters, int? Rating, uint? RowVersion);

    /// <summary>
    /// La Halqa de l'Oustaz CONNECTÉ (entrée de la tablette) : résolue depuis le compte, aucun identifiant en paramètre.
    /// Réservée au rôle Enseignant — la Direction passe par la liste des Oustaz.
    /// </summary>
    [HttpGet("my-halqa")]
    [Authorize(Roles = nameof(Role.Enseignant))]
    [ProducesResponseType<HalqaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyHalqa(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetMyHalqaQuery(), cancellationToken));

    /// <summary>La Halqa d'un Oustaz avec l'indicateur global de chaque élève. Un Oustaz ne lit que la sienne.</summary>
    [HttpGet("instructors/{instructorId:guid}/students")]
    [Authorize(Roles = HalqaReadRoles)]
    [ProducesResponseType<HalqaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInstructorStudents(Guid instructorId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInstructorStudentsQuery(instructorId), cancellationToken));

    /// <summary>Affecte (ou détache, <c>instructorId</c> null) un lot d'élèves à un Oustaz. Directeur uniquement.</summary>
    [HttpPost("students/assign-instructor")]
    [Authorize(Roles = AssignRoles)]
    [ProducesResponseType<AssignInstructorResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignInstructor(
        [FromBody] AssignInstructorRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new AssignInstructorCommand(request.InstructorId, request.StudentIds ?? []),
            cancellationToken));

    /// <summary>La grille des 60 Hizb d'un élève. Un Oustaz ne lit que les élèves de sa Halqa.</summary>
    [HttpGet("students/{studentId:guid}/hizb-progress")]
    [Authorize(Roles = HalqaReadRoles)]
    [ProducesResponseType<StudentHizbProgressDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHizbProgress(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentHizbProgressQuery(studentId), cancellationToken));

    /// <summary>Enregistre l'avancement d'un Hizb (quarts, note). Un Oustaz n'écrit que pour sa Halqa (403 sinon).</summary>
    [HttpPut("students/{studentId:guid}/hizb-progress")]
    [Authorize(Roles = HizbWriteRoles)]
    [ProducesResponseType<HizbCellDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateHizbProgress(
        Guid studentId, [FromBody] UpdateHizbProgressRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateHizbProgressCommand(
                studentId, request.HizbNumber, request.CompletedQuarters, request.Rating, request.RowVersion),
            cancellationToken));
}
