using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetRevenueConsolidationExcel;

/// <summary>
/// GET /finance/reports/revenue/excel — le MÊME rapport que GetRevenueConsolidationQuery, au format
/// comptable (.xlsx).
///
/// Réutilise la requête de consolidation au lieu de recalculer : le fichier exporté et l'écran
/// affiché doivent porter exactement les mêmes chiffres, faute de quoi un comptable les découvrirait
/// contradictoires au pire moment.
/// </summary>
public record GetRevenueConsolidationExcelQuery : IRequest<RevenueExcelFile>
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public record RevenueExcelFile(byte[] Content, string FileName);

public class GetRevenueConsolidationExcelQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IRevenueReportExcelGenerator excelGenerator)
    : IRequestHandler<GetRevenueConsolidationExcelQuery, RevenueExcelFile>
{
    public async Task<RevenueExcelFile> Handle(
        GetRevenueConsolidationExcelQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var report = await mediator.Send(
            new GetRevenueConsolidationQuery { From = request.From, To = request.To }, cancellationToken);

        // `schools` n'est pas une table tenant : le filtre SchoolId est explicite.
        var schoolName = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        var content = excelGenerator.Generate(report, schoolName);
        var fileName = $"revenus_{report.From:yyyyMMdd}_{report.To:yyyyMMdd}.xlsx";

        return new RevenueExcelFile(content, fileName);
    }
}
