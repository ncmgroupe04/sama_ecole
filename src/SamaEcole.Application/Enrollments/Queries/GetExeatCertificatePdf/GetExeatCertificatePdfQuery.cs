using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;
using MediatR;

namespace SamaEcole.Application.Enrollments.Queries.GetExeatCertificatePdf;

public record GetExeatCertificatePdfQuery(Guid EnrollmentId) : IRequest<ExeatCertificatePdfResult>, IAuditableRequest;

public record ExeatCertificatePdfResult(byte[] Content, string CertificateNumber);

public class GetExeatCertificatePdfQueryHandler(
    ISender mediator,
    IExeatCertificatePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetExeatCertificatePdfQuery, ExeatCertificatePdfResult>
{
    public async Task<ExeatCertificatePdfResult> Handle(GetExeatCertificatePdfQuery request, CancellationToken cancellationToken)
    {
        var certificate = await mediator.Send(new GetExeatCertificateQuery(request.EnrollmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(certificate.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={certificate.CertificateNumber}");

        return new ExeatCertificatePdfResult(pdfGenerator.Generate(certificate, logo, qrCode), certificate.CertificateNumber);
    }
}
