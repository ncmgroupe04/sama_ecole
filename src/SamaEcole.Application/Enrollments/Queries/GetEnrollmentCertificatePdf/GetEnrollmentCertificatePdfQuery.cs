using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using MediatR;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificatePdf;

public record GetEnrollmentCertificatePdfQuery(Guid EnrollmentId) : IRequest<CertificatePdfResult>, IAuditableRequest;

public record CertificatePdfResult(byte[] Content, string CertificateNumber);

public class GetEnrollmentCertificatePdfQueryHandler(
    ISender mediator,
    IEnrollmentCertificatePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetEnrollmentCertificatePdfQuery, CertificatePdfResult>
{
    public async Task<CertificatePdfResult> Handle(GetEnrollmentCertificatePdfQuery request, CancellationToken cancellationToken)
    {
        var certificate = await mediator.Send(new GetEnrollmentCertificateQuery(request.EnrollmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(certificate.SchoolLogoUrl, cancellationToken);

        return new CertificatePdfResult(pdfGenerator.Generate(certificate, logo), certificate.CertificateNumber);
    }
}
