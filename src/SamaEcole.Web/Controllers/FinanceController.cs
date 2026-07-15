using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Commands.ApplyStandardFee;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Application.Finance.Commands.UpdateClassFee;
using SamaEcole.Application.Finance.Queries.GetClassFees;
using SamaEcole.Application.Finance.Queries.GetFeeCategories;
using SamaEcole.Application.Finance.Queries.GetFeeHistory;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using SamaEcole.Application.Finance.Queries.GetPaymentReceiptPdf;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-F01 — paramétrage des frais (/finance/fees, /finance/fee-categories). Contrôleur
/// mince : aucune logique métier ici (AGENTS.md règle #8). L'école vient du JWT, jamais d'un
/// paramètre (règle #10).
///
/// LECTURE ouverte à tout utilisateur de l'école : le secrétariat compose les montants dus à
/// l'inscription, la finance encaisse — tous ont besoin de VOIR le barème. ÉCRITURE réservée au
/// Directeur : le barème est un paramètre d'établissement (docs/Volume_7_Security.md §15), et la
/// règle #4 tient le service Finance à l'écart de la fixation des montants.
/// </summary>
[ApiController]
[Route("api/v1/finance")]
[Authorize]
public class FinanceController(ISender mediator) : ControllerBase
{
    public record UpdateFeeRequest(decimal Amount, uint RowVersion);

    // ------------------------------------------------------------------ Catégories

    [HttpGet("fee-categories")]
    [ProducesResponseType<IReadOnlyList<FeeCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeCategoriesQuery(), cancellationToken));

    [HttpPost("fee-categories")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<CreateFeeCategoryResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateFeeCategoryCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(ListCategories), new { id = result.Id }, result);
    }

    // ------------------------------------------------------------------ Barème

    [HttpGet("fees")]
    [ProducesResponseType<IReadOnlyList<ClassFeeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFees(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassFeesQuery(), cancellationToken));

    /// <summary>Option 1 — applique un montant standard à toutes les classes d'une catégorie, en un geste.</summary>
    [HttpPost("fees/apply-standard")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<ApplyStandardFeeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyStandard(
        [FromBody] ApplyStandardFeeCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Option 2 — ajuste le montant d'une classe (exception), avec verrouillage optimiste.</summary>
    [HttpPut("fees/{id:guid}")]
    [Authorize(Roles = nameof(Role.Directeur))]
    [ProducesResponseType<UpdateClassFeeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateFee(
        Guid id, [FromBody] UpdateFeeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateClassFeeCommand(id, request.Amount, request.RowVersion), cancellationToken));

    [HttpGet("fees/{id:guid}/history")]
    [ProducesResponseType<IReadOnlyList<FeeHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FeeHistory(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeHistoryQuery(id), cancellationToken));

    // ------------------------------------------------------------------ Caisse (JGK-F02)

    // ÉCRITURE réservée au Directeur et à la Finance : c'est la Finance qui encaisse (le Secrétariat, lui,
    // compose le montant dû à l'inscription). Miroir exact d'EnrollmentsController (Directeur+Secrétariat)
    // — la règle #4 sépare qui fixe le dû de qui l'encaisse.
    private const string PaymentWriters = "Directeur,Finance";

    /// <summary>Encaisse un versement sur une inscription, avec verrou optimiste et reçu officiel (règles #4, #5).</summary>
    [HttpPost("payments")]
    [Authorize(Roles = PaymentWriters)]
    [ProducesResponseType<RecordPaymentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordPayment(
        [FromBody] RecordPaymentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(PaymentReceipt), new { id = result.PaymentId }, result);
    }

    // LECTURE du reçu ouverte à tout rôle de l'école : le tenant vient du JWT, un paiement d'une autre
    // école est introuvable (404), jamais servi.
    [HttpGet("payments/{id:guid}/receipt")]
    [ProducesResponseType<PaymentReceiptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PaymentReceipt(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPaymentReceiptQuery(id), cancellationToken));

    [HttpGet("payments/{id:guid}/receipt/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PaymentReceiptPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPaymentReceiptPdfQuery(id), cancellationToken);

        return File(result.Content, "application/pdf", $"Recu-{result.ReceiptNumber}.pdf");
    }
}
