using MediatR;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceReport;

/// <summary>
/// Ticket JGK-R02 — rapport paginé. Le calcul et la garde de sécurité (classId ⊂ tenant) vivent dans
/// <see cref="AttendanceReportAggregator"/>, partagé avec l'export R03 : ici on ne fait que découper une
/// page de la liste complète.
/// </summary>
public class GetAttendanceReportQueryHandler(AttendanceReportAggregator aggregator)
    : IRequestHandler<GetAttendanceReportQuery, AttendanceReportDto>
{
    public async Task<AttendanceReportDto> Handle(GetAttendanceReportQuery request, CancellationToken cancellationToken)
    {
        var aggregate = await aggregator.ComputeAsync(request.StartDate, request.EndDate, request.ClassId, cancellationToken);

        var pageItems = aggregate.Students
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new AttendanceReportDto(
            request.StartDate,
            request.EndDate,
            request.ClassId,
            aggregate.AverageAttendanceRate,
            pageItems,
            aggregate.Students.Count,
            request.Page,
            request.PageSize);
    }
}
