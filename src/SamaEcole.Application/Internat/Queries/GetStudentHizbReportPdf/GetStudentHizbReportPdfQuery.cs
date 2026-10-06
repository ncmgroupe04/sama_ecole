using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;
using MediatR;

namespace SamaEcole.Application.Internat.Queries.GetStudentHizbReportPdf;

/// <summary>
/// GET /api/v1/internat/students/{id}/hizb-report/pdf — le bulletin coranique d'un élève en PDF. Une impression est une
/// action tracée (<see cref="IAuditableRequest"/>), comme tous les documents officiels de l'application.
/// </summary>
public record GetStudentHizbReportPdfQuery(Guid StudentId) : IRequest<HizbReportPdfResult>, IAuditableRequest;

/// <param name="Content">Octets du PDF.</param>
/// <param name="Matricule">Matricule de l'élève, pour nommer le fichier.</param>
public record HizbReportPdfResult(byte[] Content, string Matricule);

public class GetStudentHizbReportPdfQueryHandler(
    ISender mediator,
    IHizbReportPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetStudentHizbReportPdfQuery, HizbReportPdfResult>
{
    public async Task<HizbReportPdfResult> Handle(GetStudentHizbReportPdfQuery request, CancellationToken cancellationToken)
    {
        var report = await mediator.Send(new GetStudentHizbReportQuery(request.StudentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(report.SchoolLogoUrl, cancellationToken);

        return new HizbReportPdfResult(pdfGenerator.Generate(report, logo), report.Matricule);
    }
}
