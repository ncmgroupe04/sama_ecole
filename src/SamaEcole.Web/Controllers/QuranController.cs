using SamaEcole.Application.Quran;
using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;
using SamaEcole.Application.Quran.Commands.UpdateQuranProgress;
using SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;
using SamaEcole.Application.Quran.Queries.GetClassQuranProgress;
using SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;
using SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Coran/Franco-Arabe (spec Phase 2, docs/superpowers/specs/2026-09-20-franco-arabic-cqrs-api-design.md).
/// Contrôleur mince, aucune logique métier (AGENTS.md règle #8). `[RequireModule(SchoolModule.Coran)]`
/// est la SEULE garde nécessaire (décision #5 de la spec) : contrairement à Internat, aucune donnée
/// Coran ne vit sur une entité partagée accessible par un autre chemin.
/// </summary>
[ApiController]
[Route("api/v1/quran")]
[Authorize]
[RequireModule(SchoolModule.Coran)]
public class QuranController(ISender mediator) : ControllerBase
{
    public record CreateProgressRequest(
        Guid StudentId, int JuzNumber, int HizbNumber, int SurahNumber,
        QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes);

    public record UpdateProgressRequest(
        QuranMemorizationStatus Status, DateOnly? EvaluationDate, string? Notes, uint RowVersion);

    public record CreateEvaluationRequest(
        Guid StudentId, DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore);

    public record UpdateEvaluationRequest(
        DateOnly EvaluationDate, int MemoryMistakes, int TajwidMistakes, int Hesitations, decimal FinalScore, uint RowVersion);

    /// <summary>Écriture (Create/Update) : Directeur + Enseignant, comme GradesController.GradingRoles (décision #1).</summary>
    private const string WriteRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}";

    /// <summary>Lecture : Directeur + Enseignant + Secrétariat, comme GradesController.ViewGradesRoles (décision #2).</summary>
    private const string ReadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)},{nameof(Role.Secretariat)}";

    // ---------------------------------------------------------------- QuranProgress

    [HttpGet("progress")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<QuranProgressDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStudentProgress([FromQuery] Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentQuranProgressQuery(studentId), cancellationToken));

    [HttpGet("progress/classroom/{classroomId:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<ClassQuranProgressRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassProgress(Guid classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassQuranProgressQuery(classroomId), cancellationToken));

    [HttpPost("progress")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranProgressDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateProgress([FromBody] CreateProgressRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateQuranProgressCommand(
                request.StudentId, request.JuzNumber, request.HizbNumber, request.SurahNumber,
                request.Status, request.EvaluationDate, request.Notes),
            cancellationToken);

        return CreatedAtAction(nameof(GetStudentProgress), new { studentId = result.StudentId }, result);
    }

    [HttpPut("progress/{id:guid}")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranProgressDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateProgress(Guid id, [FromBody] UpdateProgressRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateQuranProgressCommand(id, request.Status, request.EvaluationDate, request.Notes, request.RowVersion),
            cancellationToken));

    // ---------------------------------------------------------------- QuranEvaluation

    [HttpGet("evaluations")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<QuranEvaluationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStudentEvaluations([FromQuery] Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentQuranEvaluationsQuery(studentId), cancellationToken));

    [HttpGet("evaluations/classroom/{classroomId:guid}")]
    [Authorize(Roles = ReadRoles)]
    [ProducesResponseType<IReadOnlyList<ClassQuranEvaluationRowDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassEvaluations(Guid classroomId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassQuranEvaluationsQuery(classroomId), cancellationToken));

    [HttpPost("evaluations")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranEvaluationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateEvaluation([FromBody] CreateEvaluationRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CreateQuranEvaluationCommand(
                request.StudentId, request.EvaluationDate, request.MemoryMistakes,
                request.TajwidMistakes, request.Hesitations, request.FinalScore),
            cancellationToken);

        return CreatedAtAction(nameof(GetStudentEvaluations), new { studentId = result.StudentId }, result);
    }

    [HttpPut("evaluations/{id:guid}")]
    [Authorize(Roles = WriteRoles)]
    [ProducesResponseType<QuranEvaluationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateEvaluation(Guid id, [FromBody] UpdateEvaluationRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateQuranEvaluationCommand(
                id, request.EvaluationDate, request.MemoryMistakes, request.TajwidMistakes,
                request.Hesitations, request.FinalScore, request.RowVersion),
            cancellationToken));
}
