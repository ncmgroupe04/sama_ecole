using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDebtorAgingReport;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetDebtorAgingExport;

/// <summary>
/// GET /finance/reports/debtor-aging/excel — le MÊME rapport que GetDebtorAgingReportQuery, au format
/// comptable (.xlsx). Réutilise la requête au lieu de recalculer, même principe que
/// GetRevenueConsolidationExcelQuery.
/// </summary>
public record GetDebtorAgingExportQuery : IRequest<DebtorAgingExcelFile>;

public record DebtorAgingExcelFile(byte[] Content, string FileName);

public class GetDebtorAgingExportQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IDebtorAgingExcelGenerator excelGenerator)
    : IRequestHandler<GetDebtorAgingExportQuery, DebtorAgingExcelFile>
{
    public async Task<DebtorAgingExcelFile> Handle(
        GetDebtorAgingExportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var report = await mediator.Send(new GetDebtorAgingReportQuery(), cancellationToken);

        // `schools` n'est pas une table tenant : le filtre SchoolId est explicite.
        var schoolName = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        var content = excelGenerator.Generate(report, schoolName);
        var fileName = $"debiteurs_{report.GeneratedOn:yyyyMMdd}.xlsx";

        return new DebtorAgingExcelFile(content, fileName);
    }
}
