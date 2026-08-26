using SamaEcole.Application.Exams.Commands.AssignExamCenter;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Application.Exams.Commands.CreateExamSession;
using SamaEcole.Application.Exams.Commands.DispatchExamConvocations;
using SamaEcole.Application.Exams.Commands.RecordExamResult;
using SamaEcole.Application.Exams.Commands.TransmitExamDossier;
using SamaEcole.Application.Exams.Commands.UpdateExamDossier;
using SamaEcole.Application.Exams.Commands.UpdateExamSession;
using SamaEcole.Application.Exams.Queries.GetExamCandidateFormPdf;
using SamaEcole.Application.Exams.Queries.GetExamCandidateFormsBatchPdf;
using SamaEcole.Application.Exams.Queries.GetExamConvocationPdf;
using SamaEcole.Application.Exams.Queries.GetExamDossierAudit;
using SamaEcole.Application.Exams.Queries.GetExamDossierDetail;
using SamaEcole.Application.Exams.Queries.GetExamDossiers;
using SamaEcole.Application.Exams.Queries.GetExamRegistrationExport;
using SamaEcole.Application.Exams.Queries.GetExamSessions;
using SamaEcole.Application.Exams.Queries.GetExamStatistics;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Examens officiels (CFEE/BFEM/BAC) — /exams. Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// Matrice de droits (docs/Volume_4_API_Design.md §22) : `Directeur` et `Secretariat` uniquement,
/// LECTURE COMPRISE — un dossier porte des données d'état civil sensibles (extrait de naissance).
/// L'ouverture au rôle `Enseignant`, restreinte à ses classes assignées, est un ticket séparé
/// (JGK-J08, non livré) : ne pas élargir cette matrice sans le filtre par classe qui va avec.
///
/// Couvre l'intégralité du backlog Module J (JGK-J01 à J07) : sessions, dossiers, contrôle d'état
/// civil, audit, attribution centre/table, transmission, résultats, statistiques, fiches de
/// candidature (unitaire/lot), convocations et export ministériel.
/// </summary>
[ApiController]
[Route("api/v1/exams")]
[Authorize(Roles = Roles)]
public class ExamsController(ISender mediator) : ControllerBase
{
    private const string Roles = "Directeur,Secretariat";

    // ------------------------------------------------------------------ Sessions

    [HttpGet("sessions")]
    [ProducesResponseType<IReadOnlyList<ExamSessionResult>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSessions(
        [FromQuery] GetExamSessionsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpPost("sessions")]
    [ProducesResponseType<ExamSessionResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateSession(
        [FromBody] CreateExamSessionCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(ListSessions), new { id = result.Id }, result);
    }

    public record UpdateExamSessionRequest(string? CenterName, Domain.Enums.ExamSessionStatus Status, uint RowVersion);

    [HttpPut("sessions/{id:guid}")]
    [ProducesResponseType<ExamSessionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSession(
        Guid id, [FromBody] UpdateExamSessionRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateExamSessionCommand
            {
                Id = id,
                CenterName = request.CenterName,
                Status = request.Status,
                RowVersion = request.RowVersion
            },
            cancellationToken));

    // ------------------------------------------------------------------ Dossiers

    [HttpGet("dossiers")]
    [ProducesResponseType<PaginatedExamDossiers>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDossiers(
        [FromQuery] GetExamDossiersQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpGet("dossiers/audit")]
    [ProducesResponseType<IReadOnlyList<ExamDossierAuditEntry>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AuditDossiers(
        [FromQuery] Guid examSessionId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExamDossierAuditQuery(examSessionId), cancellationToken));

    [HttpGet("dossiers/{id:guid}")]
    [ProducesResponseType<ExamDossierDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDossier(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExamDossierDetailQuery(id), cancellationToken));

    [HttpPost("dossiers")]
    [ProducesResponseType<ExamDossierResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateDossier(
        [FromBody] CreateExamDossierCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetDossier), new { id = result.Id }, result);
    }

    public record UpdateExamDossierRequest(
        string? ExamCenterName,
        string? BirthCertificateNumber,
        bool BirthCertificatePresent,
        bool? CivilStatusConforming,
        string? CivilStatusNotes,
        uint RowVersion);

    [HttpPut("dossiers/{id:guid}")]
    [ProducesResponseType<ExamDossierResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateDossier(
        Guid id, [FromBody] UpdateExamDossierRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateExamDossierCommand
            {
                Id = id,
                ExamCenterName = request.ExamCenterName,
                BirthCertificateNumber = request.BirthCertificateNumber,
                BirthCertificatePresent = request.BirthCertificatePresent,
                CivilStatusConforming = request.CivilStatusConforming,
                CivilStatusNotes = request.CivilStatusNotes,
                RowVersion = request.RowVersion
            },
            cancellationToken));

    public record AssignExamCenterRequest(string? ExamCenterName, string? CandidateNumber, uint RowVersion);

    /// <summary>Le numéro de table est généré DANS la transaction de ce Handler — jamais à la création du dossier.</summary>
    [HttpPost("dossiers/{id:guid}/assign-center")]
    [ProducesResponseType<ExamDossierResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignCenter(
        Guid id, [FromBody] AssignExamCenterRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new AssignExamCenterCommand
            {
                Id = id,
                ExamCenterName = request.ExamCenterName,
                CandidateNumber = request.CandidateNumber,
                RowVersion = request.RowVersion
            },
            cancellationToken));

    /// <summary>Refusé (409) si le dossier est encore Incomplet — voir GET dossiers/audit.</summary>
    [HttpPost("dossiers/{id:guid}/transmit")]
    [ProducesResponseType<ExamDossierResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TransmitDossier(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new TransmitExamDossierCommand(id), cancellationToken));

    public record RecordExamResultRequest(bool IsAdmitted, Domain.Enums.ExamMention? Mention, decimal? AverageScore, DateOnly DeliberatedOn);

    [HttpPut("dossiers/{id:guid}/result")]
    [ProducesResponseType<ExamResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordResult(
        Guid id, [FromBody] RecordExamResultRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new RecordExamResultCommand
            {
                ExamDossierId = id,
                IsAdmitted = request.IsAdmitted,
                Mention = request.Mention,
                AverageScore = request.AverageScore,
                DeliberatedOn = request.DeliberatedOn
            },
            cancellationToken));

    // ------------------------------------------------------------------ Statistiques

    [HttpGet("statistics")]
    [ProducesResponseType<ExamStatistics>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] Guid? schoolYearId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExamStatisticsQuery(schoolYearId), cancellationToken));

    // ------------------------------------------------------------------ Documents et export

    [HttpGet("dossiers/{id:guid}/candidate-form/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCandidateFormPdf(Guid id, CancellationToken cancellationToken)
    {
        var pdf = await mediator.Send(new GetExamCandidateFormPdfQuery(id), cancellationToken);
        return File(pdf, "application/pdf");
    }

    /// <summary>Ne retient que les dossiers Complet/Transmis/Valide — voir GetExamCandidateFormsBatchPdfQuery.</summary>
    [HttpPost("dossiers/candidate-forms/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetCandidateFormsBatchPdf(
        [FromBody] GetExamCandidateFormsBatchPdfQuery query, CancellationToken cancellationToken)
    {
        var pdf = await mediator.Send(query, cancellationToken);
        return File(pdf, "application/pdf");
    }

    /// <summary>Refusée (409) tant que centre et numéro de table ne sont pas attribués.</summary>
    [HttpGet("dossiers/{id:guid}/convocation/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetConvocationPdf(Guid id, CancellationToken cancellationToken)
    {
        var pdf = await mediator.Send(new GetExamConvocationPdfQuery(id), cancellationToken);
        return File(pdf, "application/pdf");
    }

    /// <summary>
    /// Réutilise le canal SMS existant (Feature.SmsNotifications) — vérifié ICI (403 explicite) et de
    /// nouveau dans SmsDispatcher pour les envois hors API (AGENTS.md, même défense en profondeur que
    /// le reste de la plateforme).
    /// </summary>
    [HttpPost("sessions/{id:guid}/dispatch-convocations")]
    [RequireFeature(Feature.SmsNotifications)]
    [ProducesResponseType<DispatchExamConvocationsResult>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DispatchConvocations(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DispatchExamConvocationsCommand(id), cancellationToken);
        return Accepted(result);
    }

    [HttpGet("export/ministerial")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegistrationExport(
        [FromQuery] Guid examSessionId, CancellationToken cancellationToken)
    {
        var file = await mediator.Send(new GetExamRegistrationExportQuery(examSessionId), cancellationToken);
        return File(file.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.FileName);
    }
}
