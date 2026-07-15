using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceipt;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceiptPdf;
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
}
