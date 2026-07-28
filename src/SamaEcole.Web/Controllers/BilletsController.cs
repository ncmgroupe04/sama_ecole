using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using SamaEcole.Application.Absences.Queries.GetEntryTicketPdf;
using SamaEcole.Application.Absences.Queries.GetExitTicket;
using SamaEcole.Application.Absences.Queries.GetExitTicketPdf;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Billets d'entrée en classe (module Surveillance). Un billet est l'impression officielle A5 d'un
/// retard (<c>LateArrival</c>) déjà enregistré : le contrôleur reste mince (AGENTS.md règle #8),
/// l'école vient du JWT et la RLS isole le tenant (règle #10). Accessible à la Surveillance, au
/// Secrétariat (accueil) et à la Direction — mêmes rôles que la saisie des retards, plus le
/// Secrétariat qui délivre les billets à l'accueil.
/// </summary>
[ApiController]
[Route("api/v1/billets")]
[Authorize(Roles = $"{nameof(Role.SuperAdmin)},{nameof(Role.Directeur)},{nameof(Role.Secretariat)},{nameof(Role.Surveillant)}")]
public class BilletsController(ISender mediator, ILogger<BilletsController> logger) : ControllerBase
{
    /// <summary>Données d'un billet d'entrée pour un retard donné (aperçu avant impression).</summary>
    [HttpGet("late-arrival/{lateArrivalId:guid}")]
    [ProducesResponseType<EntryTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntryTicketDto>> GetEntryTicket(Guid lateArrivalId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetEntryTicketQuery(lateArrivalId), cancellationToken));

    /// <summary>Billet d'entrée A5 en PDF, rendu inline pour impression directe.</summary>
    [HttpGet("late-arrival/{lateArrivalId:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEntryTicketPdf(Guid lateArrivalId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetEntryTicketPdfQuery(lateArrivalId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le billet d'entrée PDF généré est vide pour le retard {LateArrivalId}", lateArrivalId);
                return NotFound(new { message = "Le billet d'entrée PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Billet-{result.TicketNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du billet d'entrée PDF pour le retard {LateArrivalId}", lateArrivalId);
            return Problem(detail: ex.Message, title: "Erreur de génération du billet d'entrée PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Données d'un billet de sortie pour une sortie anticipée donnée (aperçu avant impression).</summary>
    [HttpGet("early-departure/{earlyDepartureId:guid}")]
    [ProducesResponseType<ExitTicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExitTicketDto>> GetExitTicket(Guid earlyDepartureId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExitTicketQuery(earlyDepartureId), cancellationToken));

    /// <summary>Billet de sortie A5 en PDF, rendu inline pour impression directe.</summary>
    [HttpGet("early-departure/{earlyDepartureId:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExitTicketPdf(Guid earlyDepartureId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetExitTicketPdfQuery(earlyDepartureId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le billet de sortie PDF généré est vide pour la sortie {EarlyDepartureId}", earlyDepartureId);
                return NotFound(new { message = "Le billet de sortie PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Billet-Sortie-{result.TicketNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du billet de sortie PDF pour la sortie {EarlyDepartureId}", earlyDepartureId);
            return Problem(detail: ex.Message, title: "Erreur de génération du billet de sortie PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
