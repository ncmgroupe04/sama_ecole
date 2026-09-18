using SamaEcole.Application.Finance.Queries.GetDebtorAgingExport;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidationExcel;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Rapports financiers avancés — consolidation des revenus et exports comptables.
///
/// Contrôleur SÉPARÉ de FinanceController, et non quelques actions de plus dedans : tout ce qui est
/// ici est verrouillé par <see cref="Feature.AdvancedFinancialReports"/> (formules Standard et
/// Premium), alors que le module Finance de base reste accessible à toutes les formules. Le
/// verrouillage porté par la CLASSE rend cette frontière lisible — et impossible à oublier sur une
/// action ajoutée plus tard.
///
/// Directeur et Finance uniquement, comme le tableau de bord financier : c'est la santé financière
/// agrégée de l'établissement.
/// </summary>
[ApiController]
[Route("api/v1/finance/reports")]
[Authorize(Roles = $"{nameof(Role.Directeur)},{nameof(Role.Finance)}")]
[RequireFeature(Feature.AdvancedFinancialReports)]
[RequireModule(SchoolModule.Finance)]
public class FinancialReportsController(ISender mediator) : ControllerBase
{
    [HttpGet("revenue")]
    [ProducesResponseType<RevenueConsolidationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueConsolidation(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetRevenueConsolidationQuery { From = from, To = to }, cancellationToken));

    /// <summary>Même rapport, au format comptable téléchargeable (.xlsx).</summary>
    [HttpGet("revenue/excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRevenueConsolidationExcel(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var file = await mediator.Send(
            new GetRevenueConsolidationExcelQuery { From = from, To = to }, cancellationToken);

        return File(
            file.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.FileName);
    }

    /// <summary>Liste des débiteurs (Étape 5), avec le nombre de jours de retard — Volume 1 §7.5.</summary>
    [HttpGet("debtor-aging")]
    [ProducesResponseType<DebtorAgingReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDebtorAgingReport(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetDebtorAgingReportQuery(), cancellationToken));

    /// <summary>Même rapport, au format comptable téléchargeable (.xlsx).</summary>
    [HttpGet("debtor-aging/excel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDebtorAgingExcel(CancellationToken cancellationToken)
    {
        var file = await mediator.Send(new GetDebtorAgingExportQuery(), cancellationToken);

        return File(
            file.Content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            file.FileName);
    }
}
