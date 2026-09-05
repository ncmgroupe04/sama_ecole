using SamaEcole.Application.Finance;
using SamaEcole.Application.Finance.Queries.GetTreasuryDashboard;
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
using SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Application.Finance.Commands.OpenCashierSession;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using SamaEcole.Application.Finance.Queries.GetCurrentCashierSession;
using SamaEcole.Application.Finance.Queries.GetDailyClosingReportPdf;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using SamaEcole.Application.Finance.Commands.CloseEmployeeContract;
using SamaEcole.Application.Finance.Queries.GetEmployeeContracts;
using SamaEcole.Application.Finance.Queries.GetFichePaies;
using SamaEcole.Application.Finance.Queries.GetPayslipPdf;
using SamaEcole.Application.Finance.Queries.GetTaxDeclarations;
using SamaEcole.Application.Finance.Queries.GetTaxDeclarationPdf;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using SamaEcole.Application.Finance.Queries.GetDuesNoticePdf;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using SamaEcole.Application.Finance.Queries.GetWorkCertificatePdf;
using SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitmentPdf;
using SamaEcole.Application.Finance.Commands.CreateTeacherHourRecord;
using SamaEcole.Application.Finance.Queries.GetTeacherHourRecords;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheetPdf;
using SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;
using SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;
using SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;
using SamaEcole.Application.Finance.Commands.SendDebtorReminderBatch;
using SamaEcole.Application.Finance.Commands.DismissDebtorReminderBatch;
using SamaEcole.Application.Finance.Commands.GenerateDebtorReminderBatches;
using SamaEcole.Application.Finance.Queries.GetDebtorReminderBatches;
using SamaEcole.Domain.Enums;

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

    // ENCAISSEMENT (JGK-F02 + inscription au guichet) : Directeur, Finance ET Secrétariat. Le Secrétariat
    // tient sa propre caisse — il ouvre une session, encaisse (à l'inscription ou ici), la clôture avec
    // comptage physique. La règle #4 n'interdit pas au Secrétariat d'encaisser : elle sépare qui FIXE le
    // dû (Secrétariat/Directeur, jamais Finance) de la santé financière AGRÉGÉE (liste globale des
    // paiements, dashboards, trésorerie), qui reste réservée à Directeur/Finance ci-dessous.
    private const string PaymentWriters = "Directeur,Finance,Secretariat";

    /// <summary>Opérations de caisse (session + encaissement), ouvertes à quiconque tient une caisse.</summary>
    private const string CashierRoles = "Directeur,Finance,Secretariat";

    /// <summary>
    /// Point d'entrée de l'écran caisse : à partir d'un élève trouvé par recherche, l'inscription et le
    /// solde de l'année active. LECTURE ouverte à tout rôle de l'école, comme le reçu.
    /// </summary>
    [HttpGet("students/{studentId:guid}/balance")]
    [ProducesResponseType<StudentBalanceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StudentBalance(Guid studentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetStudentBalanceQuery(studentId), cancellationToken));

    /// <summary>
    /// Recherche élève DÉDIÉE à la caisse (GET /finance/students/search?q=...) : mêmes résultats que
    /// la recherche générique (nom/matricule), mais chaque ligne porte déjà le solde de l'inscription
    /// active — la caissière voit tout de suite qu'un élève vient d'être inscrit par le secrétariat
    /// sans avoir encore rien versé, sans devoir ouvrir sa fiche pour le découvrir.
    /// </summary>
    [HttpGet("students/search")]
    [ProducesResponseType<IReadOnlyList<CashierStudentSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchStudentsForCashier([FromQuery] string q, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SearchStudentsForCashierQuery(q ?? string.Empty), cancellationToken));

    /// <summary>
    /// Liste paginée et filtrée des encaissements de l'établissement (GET /finance/payments). Réservé
    /// à Directeur et Finance, comme Dashboard/TreasuryDashboard : contrairement à un reçu individuel
    /// (StudentBalance/PaymentReceipt, dont le tenant vient du JWT et qui ne révèle qu'un paiement
    /// précis), cette liste expose tous les encaissements de l'école — montants, méthodes, élèves.
    /// </summary>
    [HttpGet("payments")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<PaginatedPayments>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération du reçu PDF", statusCode: StatusCodes.Status500InternalServerError);
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

    /// <summary>
    /// La session ouverte de l'utilisateur courant, ou 200 avec un corps vide s'il n'en a aucune —
    /// c'est ce que /caisse interroge à l'ouverture pour savoir s'il faut proposer d'ouvrir une session
    /// ou afficher le statut de celle déjà ouverte (ticket JGK — câblage écran, 27/08/2026).
    /// </summary>
    [HttpGet("sessions/current")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType<CurrentCashierSessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentSession(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetCurrentCashierSessionQuery(), cancellationToken));

    [HttpPost("sessions/open")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenSession([FromBody] OpenCashierSessionCommand command, CancellationToken cancellationToken)
    {
        var sessionId = await mediator.Send(command, cancellationToken);
        return Ok(sessionId);
    }

    public record CloseCashierSessionRequest(decimal ActualCashAmount, string? DiscrepancyReason = null);

    /// <summary>Le comptage physique (JGK-F09) est obligatoire ; le motif d'écart ne l'est que si un écart est réellement constaté (422 sinon).</summary>
    [HttpPost("sessions/{id}/close")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType<CloseCashierSessionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseSession(
        Guid id, [FromBody] CloseCashierSessionRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CloseCashierSessionCommand(id, request.ActualCashAmount, request.DiscrepancyReason),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("sessions/{id}/closing-report")]
    [Authorize(Roles = CashierRoles)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    public async Task<IActionResult> GetClosingReportPdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDailyClosingReportPdfQuery(id), cancellationToken);

        // `inline` : le rapport de clôture s'ouvre dans la modale d'aperçu (impression / téléchargement
        // au choix depuis l'en-tête), au lieu d'atterrir directement dans les téléchargements.
        return this.InlinePdf(result.Content, result.FileName);
    }

    // ------------------------------------------------------------------ Module Comptabilité & Fiscalité (JGK)

    [HttpGet("treasury")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<TreasuryDashboardDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TreasuryDashboard(
        [FromQuery] DateOnly? startDate, [FromQuery] DateOnly? endDate, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTreasuryDashboardQuery(startDate, endDate), cancellationToken));

    [HttpGet("employee-contracts")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<EmployeeContractDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployeeContracts(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetEmployeeContractsQuery(), cancellationToken));

    [HttpPost("employee-contracts")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateEmployeeContract([FromBody] CreateEmployeeContractCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetEmployeeContracts), new { id }, id);
    }

    public record UpdateEmployeeContractRequest(
        decimal BaseSalary,
        decimal HourlyRate,
        decimal TransportAllowance,
        string Reason,
        uint RowVersion,
        PayoutMethod PayoutMethod = PayoutMethod.Cash,
        string? PayoutAccountReference = null);

    /// <summary>Augmentation de salaire ou révision du taux horaire (Volume 1 §14.1) — la seule voie de modification d'un contrat ACTIF.</summary>
    [HttpPatch("employee-contracts/{id:guid}")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<UpdateEmployeeContractResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateEmployeeContract(
        Guid id, [FromBody] UpdateEmployeeContractRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateEmployeeContractCommand(
                id, request.BaseSalary, request.HourlyRate, request.TransportAllowance, request.Reason, request.RowVersion,
                request.PayoutMethod, request.PayoutAccountReference),
            cancellationToken);

        return Ok(result);
    }

    public record CloseEmployeeContractRequest(DateOnly EndDate, string Reason, uint RowVersion);

    /// <summary>Clôture définitive d'un contrat (Volume 1 §14.1) — jamais une suppression, voir CloseEmployeeContractCommand.</summary>
    [HttpPost("employee-contracts/{id:guid}/close")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<CloseEmployeeContractResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CloseEmployeeContract(
        Guid id, [FromBody] CloseEmployeeContractRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new CloseEmployeeContractCommand(id, request.EndDate, request.Reason, request.RowVersion),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("payroll")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<FichePaieListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayroll([FromQuery] int? month, [FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFichePaiesQuery(month, year), cancellationToken));

    [HttpPost("payroll")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> GeneratePayroll([FromBody] SamaEcole.Application.Finance.Commands.GenerateFichePaie.GenerateFichePaieCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GeneratePayroll), new { id }, id);
    }

    [HttpGet("payroll/{id:guid}/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayslipPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetPayslipPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("Le bulletin de paie PDF généré est vide pour la fiche {FichePaieId}", id);
                return NotFound(new { message = "Le bulletin de paie PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Bulletin-{result.PayslipNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération du bulletin de paie PDF pour {FichePaieId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération du bulletin de paie PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("tax-declarations")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<TaxDeclarationListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaxDeclarations([FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTaxDeclarationsQuery(year), cancellationToken));

    [HttpPost("tax-declaration")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> GenerateTaxDeclaration([FromBody] SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration.GenerateTaxDeclarationCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GenerateTaxDeclaration), new { id }, id);
    }

    [HttpGet("tax-declarations/{id:guid}/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaxDeclarationPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetTaxDeclarationPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La déclaration fiscale PDF générée est vide pour {TaxDeclarationId}", id);
                return NotFound(new { message = "La déclaration fiscale PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Declaration-Fiscale-{result.DeclarationNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la déclaration fiscale PDF pour {TaxDeclarationId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la déclaration fiscale PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Module Documents administratifs (cahier des charges élite)

    // Sommation pour impayés : refuse (409, BusinessRuleException) pour un compte à jour — voir GetDuesNoticeQueryHandler.
    [HttpGet("enrollments/{enrollmentId:guid}/dues-notice")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<DuesNoticeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DuesNotice(Guid enrollmentId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDuesNoticeQuery(enrollmentId), cancellationToken));

    [HttpGet("enrollments/{enrollmentId:guid}/dues-notice/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DuesNoticePdf(Guid enrollmentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetDuesNoticePdfQuery(enrollmentId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La sommation PDF générée est vide pour l'inscription {EnrollmentId}", enrollmentId);
                return NotFound(new { message = "La sommation PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Sommation-{result.NoticeNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (SamaEcole.Application.Common.Exceptions.BusinessRuleException)
        {
            throw; // Laisse le middleware d'exception le gérer (409)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la sommation PDF pour {EnrollmentId}", enrollmentId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la sommation PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Attestation de travail : document RH interne (comme le bulletin de paie), pas de bandeau M.E.N.
    [HttpGet("employee-contracts/{contractId:guid}/work-certificate")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<WorkCertificateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WorkCertificate(Guid contractId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetWorkCertificateQuery(contractId), cancellationToken));

    [HttpGet("employee-contracts/{contractId:guid}/work-certificate/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> WorkCertificatePdf(Guid contractId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetWorkCertificatePdfQuery(contractId), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("L'attestation de travail PDF générée est vide pour le contrat {ContractId}", contractId);
                return NotFound(new { message = "L'attestation de travail PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Attestation-Travail-{result.CertificateNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de l'attestation de travail PDF pour {ContractId}", contractId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de l'attestation de travail PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Engagement financier : trace écrite d'un échéancier, ne touche jamais TotalDue/AmountPaid (règle #4).
    [HttpPost("commitments")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateFinancialCommitment([FromBody] CreateFinancialCommitmentCommand command, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetFinancialCommitment), new { id }, id);
    }

    [HttpGet("commitments/{id:guid}")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<FinancialCommitmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFinancialCommitment(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetFinancialCommitmentQuery(id), cancellationToken));

    [HttpGet("commitments/{id:guid}/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFinancialCommitmentPdf(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetFinancialCommitmentPdfQuery(id), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("L'engagement financier PDF généré est vide pour {CommitmentId}", id);
                return NotFound(new { message = "L'engagement financier PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Engagement-{result.CommitmentNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de l'engagement financier PDF pour {CommitmentId}", id);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de l'engagement financier PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // Fiche de suivi des heures (Vacataire) : détail jour par jour, distinct du calcul de paie lui-même.
    [HttpPost("employee-contracts/{contractId:guid}/hour-records")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateHourRecord(Guid contractId, [FromBody] CreateHourRecordRequest request, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(new CreateTeacherHourRecordCommand(contractId, request.Date, request.Hours, request.Note), cancellationToken);
        return CreatedAtAction(nameof(GetHourRecords), new { contractId }, id);
    }

    public record CreateHourRecordRequest(DateOnly Date, decimal Hours, string? Note);

    [HttpGet("employee-contracts/{contractId:guid}/hour-records")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<TeacherHourRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHourRecords(Guid contractId, [FromQuery] int? month, [FromQuery] int? year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetTeacherHourRecordsQuery(contractId, month, year), cancellationToken));

    [HttpGet("employee-contracts/{contractId:guid}/hour-records/sheet")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<HourRecordSheetDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHourRecordSheet(Guid contractId, [FromQuery] int month, [FromQuery] int year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetHourRecordSheetQuery(contractId, month, year), cancellationToken));

    /// <summary>
    /// Suggestion d'heures pour la paie du vacataire (ticket JGK-K01) — consultative, n'écrit rien.
    /// La Direction pré-remplit HoursWorked avec cette valeur côté client puis soumet
    /// POST /finance/payroll (GenerateFichePaieCommand) sans aucun changement de contrat sur celle-ci.
    /// </summary>
    [HttpGet("employee-contracts/{contractId:guid}/suggested-hours")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<SuggestedPayrollHoursDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSuggestedPayrollHours(
        Guid contractId, [FromQuery] int month, [FromQuery] int year, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetSuggestedPayrollHoursQuery(contractId, month, year), cancellationToken));

    [HttpGet("employee-contracts/{contractId:guid}/hour-records/sheet/pdf")]
    [Authorize(Roles = "Directeur,Finance")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHourRecordSheetPdf(Guid contractId, [FromQuery] int month, [FromQuery] int year, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new GetHourRecordSheetPdfQuery(contractId, month, year), cancellationToken);
            if (result?.Content == null || result.Content.Length == 0)
            {
                logger.LogWarning("La fiche d'heures PDF générée est vide pour le contrat {ContractId}", contractId);
                return NotFound(new { message = "La fiche d'heures PDF est introuvable ou vide." });
            }

            Response.Headers["Content-Disposition"] = $"inline; filename=\"Fiche-Heures-{result.SheetNumber}.pdf\"";
            return File(result.Content, "application/pdf");
        }
        catch (KeyNotFoundException)
        {
            throw; // Laisse le middleware d'exception le gérer (404)
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erreur lors de la génération de la fiche d'heures PDF pour le contrat {ContractId}", contractId);
            return Problem(detail: "Une erreur interne est survenue lors de la génération du document.", title: "Erreur de génération de la fiche d'heures PDF", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ------------------------------------------------------------------ Échéanciers personnalisés (Étape 5)

    [HttpPost("enrollments/{enrollmentId:guid}/installment-plan")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateFeeInstallmentPlan(
        Guid enrollmentId, [FromBody] CreateFeeInstallmentPlanRequest request, CancellationToken cancellationToken)
    {
        var id = await mediator.Send(
            new CreateFeeInstallmentPlanCommand(enrollmentId, request.Reason, request.Installments),
            cancellationToken);

        // Pas de route "GET plan par id" dédiée : le plan se consulte via le solde de l'élève
        // (GetStudentBalanceQuery), qui l'intègre déjà — un Location vers une autre ressource
        // (studentId, pas enrollmentId) serait trompeur.
        return StatusCode(StatusCodes.Status201Created, id);
    }

    public record CreateFeeInstallmentPlanRequest(string? Reason, IReadOnlyList<CreateFeeInstallmentLine> Installments);

    [HttpPost("classrooms/{classroomId:guid}/installment-plan")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<ApplyFeeInstallmentPlanToClassroomResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApplyFeeInstallmentPlanToClassroom(
        Guid classroomId, [FromBody] ApplyFeeInstallmentPlanToClassroomRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ApplyFeeInstallmentPlanToClassroomCommand(classroomId, request.Reason, request.Template),
            cancellationToken));

    public record ApplyFeeInstallmentPlanToClassroomRequest(string? Reason, IReadOnlyList<InstallmentTemplateLine> Template);

    // ------------------------------------------------------------------ Recouvrement — lots de relance (Étape 5)

    [HttpGet("dues-reminder-batches")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<List<DebtorReminderBatchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDebtorReminderBatches(
        [FromQuery] DebtorReminderBatchStatus? status, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDebtorReminderBatchesQuery(status), cancellationToken));

    /// <summary>
    /// Force le recalcul immédiat des brouillons (normalement calculés chaque nuit par
    /// DebtorAgingHostedService) — utile pour ne pas attendre le tour suivant après un réglage du
    /// seuil de retard, par exemple.
    /// </summary>
    [HttpPost("dues-reminder-batches/recompute")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecomputeDebtorReminderBatches(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GenerateDebtorReminderBatchesCommand(), cancellationToken));

    [HttpPost("dues-reminder-batches/{id:guid}/send")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType<SendDebtorReminderBatchResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SendDebtorReminderBatch(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SendDebtorReminderBatchCommand(id), cancellationToken));

    [HttpPost("dues-reminder-batches/{id:guid}/dismiss")]
    [Authorize(Roles = "Directeur,Finance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DismissDebtorReminderBatch(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DismissDebtorReminderBatchCommand(id), cancellationToken);
        return NoContent();
    }
}

