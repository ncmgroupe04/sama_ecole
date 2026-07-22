using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Enrollments.Commands.CancelEnrollment;
using SamaEcole.Application.Enrollments.Commands.ChangeEnrollmentStatus;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificatePdf;
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
public class EnrollmentsController(ISender mediator) : ControllerBase
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
        var result = await mediator.Send(new GetEnrollmentReceiptPdfQuery(id), cancellationToken);

        return File(result.Content, "application/pdf", $"Recu-{result.ReceiptNumber}.pdf");
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
        var result = await mediator.Send(new GetEnrollmentCertificatePdfQuery(id), cancellationToken);

        return File(result.Content, "application/pdf", $"Certificat-{result.CertificateNumber}.pdf");
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
