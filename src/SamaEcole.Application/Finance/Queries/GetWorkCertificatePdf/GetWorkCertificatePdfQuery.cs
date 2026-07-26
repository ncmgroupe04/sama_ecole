using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetWorkCertificatePdf;

public record GetWorkCertificatePdfQuery(Guid ContractId) : IRequest<WorkCertificatePdfResult>, IAuditableRequest;

public record WorkCertificatePdfResult(byte[] Content, string CertificateNumber);

public class GetWorkCertificatePdfQueryHandler(
    ISender mediator,
    IWorkCertificatePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetWorkCertificatePdfQuery, WorkCertificatePdfResult>
{
    public async Task<WorkCertificatePdfResult> Handle(GetWorkCertificatePdfQuery request, CancellationToken cancellationToken)
    {
        var certificate = await mediator.Send(new GetWorkCertificateQuery(request.ContractId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(certificate.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={certificate.CertificateNumber}");

        return new WorkCertificatePdfResult(pdfGenerator.Generate(certificate, logo, qrCode), certificate.CertificateNumber);
    }
}
