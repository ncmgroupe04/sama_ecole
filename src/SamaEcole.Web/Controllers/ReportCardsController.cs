using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Commands.ApplyCouncilDecisionProposals;
using SamaEcole.Application.ReportCards.Commands.UpdateCouncilRules;
using SamaEcole.Application.ReportCards.Commands.UpsertReportCardRemark;
using SamaEcole.Application.ReportCards.Queries.GetClassAnnualDeliberationPdf;
using SamaEcole.Application.ReportCards.Queries.GetCouncilRules;
using SamaEcole.Application.ReportCards.Queries.GetClassDeliberationPdf;
using SamaEcole.Application.ReportCards.Queries.GetClassReportCardsPdf;
using SamaEcole.Application.ReportCards.Queries.GetClassReportCardsZip;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Application.ReportCards.Queries.GetReportCardRemark;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-G03 — /report-cards. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// Générer/imprimer/télécharger (individuel ou groupé) est ouvert au Directeur, à l'Enseignant ET au
/// Secrétariat. Publier (verrouiller la saisie) n'est pas implémenté dans cette passe — aucune entité
/// ReportCard n'est persistée, chaque appel régénère le bulletin à partir des notes actuelles.
///
/// La distinction du conseil (Blâme… Félicitations) et les observations sont ouvertes au Directeur,
/// à l'Enseignant ET au Secrétariat, qui assure ainsi le suivi administratif de la vie scolaire au
/// même titre que la direction (docs/Volume_7_Security.md §15).
/// </summary>
[ApiController]
[Route("api/v1/report-cards")]
[Authorize]
[RequireModule(SchoolModule.Pedagogy)]
public class ReportCardsController(ISender mediator, ILogger<ReportCardsController> logger) : ControllerBase
{
    private const string ReportCardWriterRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)},{nameof(Role.Secretariat)}";
    private const string ReportCardDownloadRoles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)},{nameof(Role.Secretariat)}";
    private const string CouncilDecisionRoles = $"{nameof(Role.Directeur)},{nameof(Role.Secretariat)}";

    public record GenerateReportCardRequest(Guid StudentId, Guid TermId);
    public record UpsertReportCardRemarkRequest(
        Guid StudentId, Guid TermId, DisciplinaryMention? DisciplinaryMention, CouncilDecision? CouncilDecision, string? Observations);
    public record SendReportCardRequest(Guid StudentId, Guid TermId, SamaEcole.Application.ReportCards.Commands.SendReportCard.CommunicationChannel Channel);

    [HttpPost("generate")]
    [Authorize(Roles = ReportCardDownloadRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateReportCardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetReportCardPdfQuery(request.StudentId, request.TermId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le bulletin PDF généré est vide (Élève: {StudentId}, Trimestre: {TermId})", request.StudentId, request.TermId);
                return NotFound(new { message = "Le bulletin de notes PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du bulletin PDF");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Une erreur est survenue lors de la génération du bulletin." });
        }
    }

    /// <summary>
    /// Envoie le bulletin d'un trimestre au tuteur (WhatsApp et/ou e-mail), + un SMS d'avis.
    ///
    /// Contrôleur mince, sans <c>catch</c> fourre-tout : ExceptionHandlingMiddleware traduit les cas
    /// métier (422 tuteur sans contact, 404 élève/trimestre inconnu, 502 rejet de Meta). L'ancien
    /// <c>catch (Exception)</c> masquait TOUT en « Une erreur est survenue » — un tuteur sans numéro
    /// et un jeton WhatsApp expiré rendaient le même 500 opaque.
    ///
    /// Réponse 200 : <c>{ status, message }</c>. <c>status = "simulated"</c> quand le canal WhatsApp
    /// n'est pas configuré (bulletin JOURNALISÉ, pas transmis) — le client l'affiche en avertissement,
    /// jamais en confirmation.
    /// </summary>
    [HttpPost("send")]
    [Authorize(Roles = ReportCardDownloadRoles)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendReportCard([FromBody] SendReportCardRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new SamaEcole.Application.ReportCards.Commands.SendReportCard.SendReportCardCommand(
                request.StudentId, request.TermId, request.Channel),
            cancellationToken);

        return Ok(new
        {
            status = result.WhatsAppSimulated ? "simulated" : "sent",
            message = result.Message,
        });
    }


    /// <summary>
    /// Bulletins de toute une classe pour un trimestre, en archive ZIP (un PDF par élève) — pratique pour
    /// un envoi par e-mail ou une sauvegarde. Mêmes rôles et même limite de débit que la génération
    /// individuelle : chaque appel régénère TOUS les bulletins de la classe à la volée.
    /// </summary>
    [HttpGet("class-bulletins/zip")]
    [Authorize(Roles = ReportCardDownloadRoles)]
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
    [Authorize(Roles = ReportCardDownloadRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadClassBulletinsMergedPdf(
        [FromQuery] Guid classroomId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetClassReportCardsPdfQuery(classroomId, termId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Les bulletins fusionnés PDF sont vides (Classe: {ClassroomId}, Trimestre: {TermId})", classroomId, termId);
                return NotFound(new { message = "Le document PDF des bulletins est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération des bulletins fusionnés PDF (Classe: {ClassroomId}, Trimestre: {TermId})", classroomId, termId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération des bulletins PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// PV de délibération de toute une classe pour un trimestre, généré en UN SEUL document PDF
    /// contenant les statistiques de la classe et la liste ordonnée des élèves avec leurs moyennes et mentions.
    /// </summary>
    [HttpGet("class-deliberation/pdf")]
    [Authorize(Roles = ReportCardDownloadRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadClassDeliberationPdf(
        [FromQuery] Guid classroomId, [FromQuery] Guid termId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetClassDeliberationPdfQuery(classroomId, termId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le PV de délibération PDF est vide (Classe: {ClassroomId}, Trimestre: {TermId})", classroomId, termId);
                return NotFound(new { message = "Le PV de délibération PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du PV de délibération PDF (Classe: {ClassroomId}, Trimestre: {TermId})", classroomId, termId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération du PV de délibération PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
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

    // ── Conseil de classe (Évolution N°7) ─────────────────────────────────────────────────────────────

    /// <summary>
    /// PV ANNUEL du conseil de classe : moyennes et rangs annuels, statistiques Filles/Garçons, décision de fin
    /// d'année (saisie, à défaut proposée d'après les seuils de l'école).
    /// </summary>
    [HttpGet("class-deliberation/annual/pdf")]
    [Authorize(Roles = ReportCardDownloadRoles)]
    [EnableRateLimiting(SensitiveEndpointRateLimiting.ReportCardGenerationPolicyName)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadAnnualDeliberationPdf(
        [FromQuery] Guid classroomId, [FromQuery] Guid schoolYearId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetClassAnnualDeliberationPdfQuery(classroomId, schoolYearId), cancellationToken);
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{result.FileName}\"";
        return File(result.Content, "application/pdf");
    }

    /// <summary>
    /// « Appliquer les propositions de décision » : enregistre, pour les élèves sans décision sur la période, la
    /// décision de fin d'année proposée. Ne remplace jamais une décision déjà prise. Directeur et Secrétariat.
    /// </summary>
    [HttpPost("council-decisions/apply-proposals")]
    [Authorize(Roles = CouncilDecisionRoles)]
    [ProducesResponseType<ApplyCouncilDecisionProposalsResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApplyCouncilDecisionProposals(
        [FromBody] ApplyCouncilDecisionProposalsCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Seuils du conseil de classe (sur /20) : distinctions, note éliminatoire, passage, redoublement.</summary>
    [HttpGet("council-rules")]
    [Authorize(Roles = ReportCardDownloadRoles)]
    [ProducesResponseType<CouncilRules>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCouncilRules(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCouncilRulesQuery(), cancellationToken));

    /// <summary>Règle les seuils du conseil de classe — Directeur seul.</summary>
    [HttpPut("council-rules")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<CouncilRules>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCouncilRules(
        [FromBody] UpdateCouncilRulesCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));
}
