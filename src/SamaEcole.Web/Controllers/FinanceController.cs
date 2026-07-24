using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Commands.ApplyStandardFee;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.DeleteClassFee;
using SamaEcole.Application.Finance.Commands.DeleteFeeCategory;
using SamaEcole.Application.Finance.Commands.RecordPayment;
using SamaEcole.Application.Finance.Commands.UpdateClassFee;
using SamaEcole.Application.Finance.Queries.GetClassFees;
using SamaEcole.Application.Finance.Queries.GetFeeCategories;
using SamaEcole.Application.Finance.Queries.GetFeeHistory;
using SamaEcole.Application.Finance.Queries.GetFinanceDashboard;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using SamaEcole.Application.Finance.Queries.GetPaymentReceiptPdf;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using SamaEcole.Application.Finance.Queries.GetPayments;
using SamaEcole.Application.Finance.Queries.GetStudentBalance;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Finance.Commands.OpenCashierSession;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Ticket JGK-F01 — paramétrage des frais (/finance/fees, /finance/fee-categories). Contrôleur
/// mince : aucune logique métier ici (AGENTS.md règle #8). L'école vient du JWT, jamais d'un
/// paramètre (règle #10).
///
/// LECTURE ouverte à tout utilisateur de l'école : le secrétariat compose les montants dus à
/// l'inscription, la finance encaisse — tous ont besoin de VOIR le barème. CRÉATION d'une catégorie,
/// application d'un montant standard (Option 1), MODIFICATION/SUPPRESSION d'une ligne de barème ou
/// d'une catégorie : Directeur toujours ; Finance UNIQUEMENT si le Directeur de SON école a
/// explicitement activé la délégation correspondante (SchoolSettings.AllowFinanceToModifyFees /
/// AllowFinanceToDeleteFees, matrice d'autorisation "Photoshop") — voir
/// CanModifyFeesHandler/CanDeleteFeesHandler (SamaEcole.Web.Authorization). Fermé par défaut : la
/// règle #4 tient la Finance à l'écart de la fixation des montants tant que ce choix n'a pas été fait
/// explicitement.
///
/// Créer une catégorie / appliquer un standard PARTAGENT la délégation "modifier" (pas une bascule
/// distincte) : les deux façonnent le même objet — le barème d'établissement — au même titre qu'un
/// ajustement classe par classe (Option 2). Un Directeur qui délègue "modifier les frais" délègue donc
/// aussi la mise en place initiale du barème, jamais la SUPPRESSION (délégation séparée, plus lourde
/// de conséquences).
/// </summary>
[ApiController]
[Route("api/v1/finance")]
[Authorize]
public class FinanceController(ISender mediator, ILogger<FinanceController> logger) : ControllerBase
{
    public record UpdateFeeRequest(decimal Amount, uint RowVersion);

    // ------------------------------------------------------------------ Catégories

    [HttpGet("fee-categories")]
    [ProducesResponseType<IReadOnlyList<FeeCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeCategoriesQuery(), cancellationToken));

    /// <summary>
    /// Feature D — autonomie Finance. Matrice d'autorisation "Photoshop", même délégation que
    /// UpdateFee ci-dessous (AllowFinanceToModifyFees) : Directeur toujours, Finance seulement si
    /// délégué. Remplace l'ancien [Authorize(Roles = Directeur)] codé en dur.
    /// </summary>
    [HttpPost("fee-categories")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
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

    /// <summary>
    /// Supprime (soft delete) une catégorie ET, en cascade, tout son barème — matrice d'autorisation
    /// "Photoshop" : Directeur toujours, Finance seulement si SchoolSettings.AllowFinanceToDeleteFees
    /// est activé pour cette école (CanDeleteFeesHandler).
    /// </summary>
    [HttpDelete("fee-categories/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteFeeCategoryCommand(id), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Barème

    [HttpGet("fees")]
    [ProducesResponseType<IReadOnlyList<ClassFeeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFees(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetClassFeesQuery(), cancellationToken));

    /// <summary>
    /// Option 1 — applique un montant standard à toutes les classes d'une catégorie, en un geste.
    /// Feature D : même délégation que CreateCategory/UpdateFee (AllowFinanceToModifyFees).
    /// </summary>
    [HttpPost("fees/apply-standard")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
    [ProducesResponseType<ApplyStandardFeeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyStandard(
        [FromBody] ApplyStandardFeeCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>
    /// Option 2 — ajuste le montant d'une classe (exception), avec verrouillage optimiste. Matrice
    /// d'autorisation "Photoshop" : Directeur toujours, Finance seulement si
    /// SchoolSettings.AllowFinanceToModifyFees est activé pour cette école (CanModifyFeesHandler) —
    /// remplace l'ancien [Authorize(Roles = Directeur)] codé en dur.
    /// </summary>
    [HttpPut("fees/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanModifyFees)]
    [ProducesResponseType<UpdateClassFeeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateFee(
        Guid id, [FromBody] UpdateFeeRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new UpdateClassFeeCommand(id, request.Amount, request.RowVersion), cancellationToken));

    /// <summary>
    /// Supprime (soft delete) une ligne de barème, avec le même verrouillage optimiste que la
    /// modification. Matrice d'autorisation "Photoshop" : Directeur toujours, Finance seulement si
    /// SchoolSettings.AllowFinanceToDeleteFees est activé pour cette école (CanDeleteFeesHandler).
    /// `rowVersion` en query string : une suppression, contrairement à une modification, n'a pas de
    /// corps de requête à transporter avec elle.
    /// </summary>
    [HttpDelete("fees/{id:guid}")]
    [Authorize(Policy = FinancePolicies.CanDeleteFees)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteFee(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteClassFeeCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    [HttpGet("fees/{id:guid}/history")]
    [ProducesResponseType<IReadOnlyList<FeeHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FeeHistory(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFeeHistoryQuery(id), cancellationToken));

    // ------------------------------------------------------------------ Caisse (JGK-F02)

    // ÉCRITURE réservée au Directeur et à la Finance : c'est la Finance qui encaisse (le Secrétariat, lui,
    // compose le montant dû à l'inscription). Miroir exact d'EnrollmentsController (Directeur+Secrétariat)
    // — la règle #4 sépare qui fixe le dû de qui l'encaisse.
    private const string PaymentWriters = "Directeur,Finance";

    /// <summary>
    /// Point d'entrée de l'écran caisse : à partir d'un élève trouvé par recherche, l'inscription et le
    /// solde de l'année active. LECTURE ouverte à tout rôle de l'école, comme le reçu.
    /// </summary>
    [HttpGet("students/{studentId:guid}/balance")]
    [ProducesResponseType<StudentBalanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StudentBalance(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentBalanceQuery(studentId), cancellationToken));

    /// <summary>Liste paginée et filtrée des encaissements de l'établissement (GET /finance/payments).</summary>
    [HttpGet("payments")]
    [ProducesResponseType<PaginatedPayments>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPayments(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        [FromQuery] string? method = null, [FromQuery] Guid? classroomId = null,
        CancellationToken cancellationToken = default)
        => Ok(await mediator.Send(
            new GetPaymentsQuery
            {
                Page = page,
                PageSize = pageSize,
                Search = search,
                Method = method,
                ClassroomId = classroomId
            }, cancellationToken));

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
        try
        {
            var result = await mediator.Send(new GetPaymentReceiptPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le contenu PDF généré est vide pour le paiement {PaymentId}", id);
                return NotFound(new { message = "Le document PDF de reçu est introuvable ou vide." });
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
            logger.LogError(ex, "Erreur lors de la génération du reçu PDF pour le paiement {PaymentId}", id);
            return Problem(detail: ex.Message, title: "Erreur de génération du reçu PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Tableau de bord (JGK-F04)

    /// <summary>
    /// Encaissé jour/mois/année, solde dû et taux de recouvrement sur l'année scolaire active. Réservé
    /// à Directeur et Finance : le Secrétariat compose le montant dû à l'inscription mais n'a pas besoin
    /// de voir la santé financière agrégée de l'établissement (règle #4).
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<FinanceDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFinanceDashboardQuery(), cancellationToken));

    [HttpGet("daily-cash-register/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DailyCashRegisterPdf(
        [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        try
        {
            var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var result = await mediator.Send(new GetDailyCashRegisterPdfQuery(targetDate), cancellationToken);
            
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le contenu PDF généré est vide pour le journal de caisse du {Date}", targetDate);
                return NotFound(new { message = "Le document PDF du journal de caisse est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Journal_Caisse_{targetDate:yyyy-MM-dd}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du journal de caisse PDF.");
            return Problem(
                detail: "Une erreur interne est survenue lors de la génération du document.", 
                title: "Erreur de génération", 
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Caisse Sessions

    [HttpPost("sessions/open")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenSession([FromBody] OpenCashierSessionCommand command, CancellationToken cancellationToken)
    {
        var sessionId = await mediator.Send(command, cancellationToken);
        return Ok(sessionId);
    }

    [HttpPost("sessions/{id}/close")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<CloseCashierSessionResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CloseSession(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new CloseCashierSessionCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions/{id}/closing-report")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    public async Task<IActionResult> GetClosingReportPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDailyClosingReportPdfQuery(id), cancellationToken);
        return File(result.Content, "application/pdf", result.FileName);
    }
}

