using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-G03 — /report-cards. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
///
/// Générer/imprimer est ouvert au Directeur ET à l'Enseignant (docs/Volume_7_Security.md
/// « Bulletins »). Publier (verrouiller la saisie) n'est pas implémenté dans cette passe — aucune
/// entité ReportCard n'est persistée, chaque appel régénère le bulletin à partir des notes actuelles.
/// </summary>
[ApiController]
[Route("api/v1/report-cards")]
[Authorize]
public class ReportCardsController(ISender mediator) : ControllerBase
{
    public record GenerateReportCardRequest(Guid StudentId, Guid TermId);

    [HttpPost("generate")]
    [Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Enseignant)}")]
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
}
