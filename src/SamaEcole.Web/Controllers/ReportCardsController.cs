using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Commands.UpsertReportCardRemark;
using SamaEcole.Application.ReportCards.Queries.GetClassReportCardsPdf;
using SamaEcole.Application.ReportCards.Queries.GetClassReportCardsZip;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Application.ReportCards.Queries.GetReportCardRemark;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-G03 — /report-cards. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// Générer/imprimer est ouvert au Directeur ET à l'Enseignant (docs/Volume_7_Security.md
/// « Bulletins »). Publier (verrouiller la saisie) n'est pas implémenté dans cette passe — aucune
/// entité ReportCard n'est persistée, chaque appel régénère le bulletin à partir des notes actuelles.
///
/// La distinction du conseil (Blâme… Félicitations) et les observations partagent les MÊMES rôles que
/// la génération : c'est la même préparation du même document, jamais ouverte au Secrétariat.
/// </summary>
[ApiController]
[Route("api/v1/report-cards")]
[Authorize]
public class ReportCardsController(ISender mediator) : ControllerBase
{
    private const string ReportCardWriterRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}";

    public record GenerateReportCardRequest(Guid StudentId, Guid TermId);
    public record UpsertReportCardRemarkRequest(
        Guid StudentId, Guid TermId, DisciplinaryMention? DisciplinaryMention, CouncilDecision? CouncilDecision, string? Observations);

    [HttpPost("generate")]
    [Authorize(Roles = ReportCardWriterRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateReportCardRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetReportCardPdfQuery(request.StudentId, request.TermId), cancellationToken);

        return File(result.Content, "application/pdf", result.FileName);
    }

    /// <summary>
    /// Bulletins de toute une classe pour un trimestre, en archive ZIP (un PDF par élève) — pratique pour
    /// un envoi par e-mail ou une sauvegarde. Mêmes rôles et même limite de débit que la génération
    /// individuelle : chaque appel régénère TOUS les bulletins de la classe à la volée.
    /// </summary>
    [HttpGet("class-bulletins/zip")]
    [Authorize(Roles = ReportCardWriterRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadClassBulletinsZip(
        [FromQuery] Guid classroomId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetClassReportCardsZipQuery(classroomId, termId), cancellationToken);

        return File(result.Content, "application/zip", result.FileName);
    }

    /// <summary>
    /// Bulletins de toute une classe pour un trimestre, fusionnés en UN SEUL PDF (une page A5 par élève)
    /// — pensé pour l'impression papier en lot. Mêmes rôles et même limite de débit que la génération
    /// individuelle.
    /// </summary>
    [HttpGet("class-bulletins/merged-pdf")]
    [Authorize(Roles = ReportCardWriterRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadClassBulletinsMergedPdf(
        [FromQuery] Guid classroomId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetClassReportCardsPdfQuery(classroomId, termId), cancellationToken);

        return File(result.Content, "application/pdf", result.FileName);
    }

    /// <summary>Préremplit l'écran de saisie — vide (deux null) si rien n'a encore été saisi pour ce trimestre.</summary>
    [HttpGet("remark")]
    [Authorize(Roles = ReportCardWriterRoles)]
    [ProducesResponseType<ReportCardRemarkDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRemark(
        [FromQuery] Guid studentId, [FromQuery] Guid termId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetReportCardRemarkQuery(studentId, termId), cancellationToken));

    [HttpPut("remark")]
    [Authorize(Roles = ReportCardWriterRoles)]
    [ProducesResponseType<ReportCardRemarkDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpsertRemark(
        [FromBody] UpsertReportCardRemarkRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpsertReportCardRemarkCommand(
                request.StudentId, request.TermId, request.DisciplinaryMention, request.CouncilDecision, request.Observations),
            cancellationToken));
}
