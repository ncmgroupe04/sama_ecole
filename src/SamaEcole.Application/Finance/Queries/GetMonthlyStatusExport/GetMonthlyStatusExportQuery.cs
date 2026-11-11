using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Application.Finance.Queries.GetMonthlyStatusExport;

/// <summary>
/// GET /finance/monthly-status/excel — le MÊME calcul que GetMonthlyStatusQuery (réutilisé, jamais recalculé à part),
/// donc l'export correspond exactement à l'écran, filtres compris.
/// </summary>
public record GetMonthlyStatusExportQuery(Guid SchoolYearId, int Month, Guid? ClassroomId = null)
    : IRequest<MonthlyStatusExcelFile>;

public record MonthlyStatusExcelFile(byte[] Content, string FileName);

public class GetMonthlyStatusExportQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMonthlyStatusExcelGenerator excelGenerator)
    : IRequestHandler<GetMonthlyStatusExportQuery, MonthlyStatusExcelFile>
{
    public async Task<MonthlyStatusExcelFile> Handle(
        GetMonthlyStatusExportQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var report = await mediator.Send(
            new GetMonthlyStatusQuery(request.SchoolYearId, request.Month, request.ClassroomId), cancellationToken);

        // `schools` n'est pas une table tenant : le filtre SchoolId est explicite.
        var schoolName = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        string? classroomName = null;
        if (request.ClassroomId is { } classroomId)
        {
            classroomName = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == classroomId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var content = excelGenerator.Generate(report, schoolName, classroomName);
        var fileName = $"mensualites_{report.SchoolYearLabel.Replace('/', '-')}_{request.Month:00}.xlsx";

        return new MonthlyStatusExcelFile(content, fileName);
    }
}
