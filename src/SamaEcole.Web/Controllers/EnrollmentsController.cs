using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Enrollments.Commands.CancelEnrollment;
using SamaEcole.Application.Enrollments.Commands.ChangeEnrollmentStatus;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificatePdf;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificatePdf;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceipt;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceiptPdf;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-E01 — inscriptions (/enrollments). Contrôleur mince : aucune logique métier ici
/// (AGENTS.md règle #8). L'école vient du JWT, jamais d'un paramètre (règle #10).
///
/// ÉCRITURE réservée au Directeur et au Secrétariat. Le service Finance en est VOLONTAIREMENT exclu :
/// c'est le secrétariat qui compose le montant dû à l'inscription, la finance encaisse ensuite —
/// jamais l'inverse (AGENTS.md règle #4, docs/Volume_1_Cahier_des_Charges.md §7.2). Aucune route ici
/// ne permet donc au rôle Finance de fixer ou modifier le TotalDue : c'est l'objet du test
/// d'autorisation négatif obligatoire du ticket.
///
/// LECTURE du reçu ouverte à tous les rôles de l'école : la finance encaisse sur la base de ce reçu.
/// </summary>
[ApiController]
[Route("api/v1/enrollments")]
[Authorize]
public class EnrollmentsController(ISender mediator, ILogger<EnrollmentsController> logger) : ControllerBase
{
    public record ChangeEnrollmentStatusRequest(EnrollmentStatus NewStatus, uint RowVersion);

    // Directeur + Secrétariat, jamais Finance (règle #4). Chaîne littérale : un [Authorize(Roles)]
    // n'accepte que des constantes, et les deux rôles se lisent tels quels dans le claim du JWT.
    private const string EnrollmentWriters = "Directeur,Secretariat";

    [HttpPost]
    [Authorize(Roles = EnrollmentWriters)]
    [ProducesResponseType<EnrollmentReceiptDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateEnrollmentCommand command, CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(Receipt), new { id = receipt.EnrollmentId }, receipt);
    }

    [HttpGet("{id:guid}/receipt")]
    [ProducesResponseType<EnrollmentReceiptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetEnrollmentReceiptQuery(id), cancellationToken));

    // Reçu officiel en PDF (ticket JGK-E02). Lecture ouverte à tous les rôles de l'école, comme le reçu
    // JSON : la finance encaisse sur cette base. Le tenant vient du JWT — un reçu d'une autre école
    // est introuvable (404), jamais servi.
    [HttpGet("{id:guid}/receipt/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReceiptPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetEnrollmentReceiptPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le reçu PDF généré est vide pour l'inscription {EnrollmentId}", id);
                return NotFound(new { message = "Le reçu PDF d'inscription est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Recu-{result.ReceiptNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du reçu d'inscription PDF pour {EnrollmentId}", id);
            return Problem(detail: ex.Message, title: "Erreur de génération du reçu PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{id:guid}/certificate")]
    [ProducesResponseType<EnrollmentCertificateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Certificate(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetEnrollmentCertificateQuery(id), cancellationToken));

    [HttpGet("{id:guid}/certificate/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CertificatePdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetEnrollmentCertificatePdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le certificat PDF généré est vide pour l'inscription {EnrollmentId}", id);
                return NotFound(new { message = "L'attestation PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Certificat-{result.CertificateNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du certificat PDF pour {EnrollmentId}", id);
            return Problem(detail: ex.Message, title: "Erreur de génération du certificat PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Certificat d'Exéat (VieScolaire) : n'existe que pour une inscription déjà DroppedOut/Transferred
    // (ChangeEnrollmentStatusCommand). Lecture ouverte comme le certificat de scolarité.
    [HttpGet("{id:guid}/exeat")]
    [ProducesResponseType<ExeatCertificateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Exeat(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetExeatCertificateQuery(id), cancellationToken));

    [HttpGet("{id:guid}/exeat/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExeatPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetExeatCertificatePdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("L'exéat PDF généré est vide pour l'inscription {EnrollmentId}", id);
                return NotFound(new { message = "Le certificat d'exéat PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Exeat-{result.CertificateNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de l'exéat PDF pour {EnrollmentId}", id);
            return Problem(detail: ex.Message, title: "Erreur de génération de l'exéat PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Annule une inscription saisie par ERREUR (mauvais élève, mauvaise classe…). Refusée en 409 si
    /// un paiement a déjà été encaissé (CancelEnrollmentCommandHandler) — dans ce cas, utiliser
    /// POST .../status pour déclarer un abandon ou un transfert à la place. `rowVersion` en query
    /// string, comme DELETE /grades/{id} : pas de corps de requête pour une annulation.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = EnrollmentWriters)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new CancelEnrollmentCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Déclare un abandon ('DroppedOut') ou un transfert ('Transferred') en cours d'année — l'élève
    /// quitte la scolarité pour l'avenir, mais l'inscription et son historique (notes, paiements)
    /// restent intacts. Voir ChangeEnrollmentStatusCommand pour les effets exacts.
    /// </summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = EnrollmentWriters)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeStatus(
        Guid id, [FromBody] ChangeEnrollmentStatusRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new ChangeEnrollmentStatusCommand(id, request.NewStatus, request.RowVersion), cancellationToken);
        return NoContent();
    }
}
