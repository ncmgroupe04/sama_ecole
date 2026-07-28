using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDuesNotice;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetDuesNoticePdf;

public record GetDuesNoticePdfQuery(Guid EnrollmentId) : IRequest<DuesNoticePdfResult>, IAuditableRequest;

public record DuesNoticePdfResult(byte[] Content, string NoticeNumber);

public class GetDuesNoticePdfQueryHandler(
    ISender mediator,
    IDuesNoticePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetDuesNoticePdfQuery, DuesNoticePdfResult>
{
    public async Task<DuesNoticePdfResult> Handle(GetDuesNoticePdfQuery request, CancellationToken cancellationToken)
    {
        var notice = await mediator.Send(new GetDuesNoticeQuery(request.EnrollmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(notice.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={notice.NoticeNumber}");

        return new DuesNoticePdfResult(pdfGenerator.Generate(notice, logo, qrCode), notice.NoticeNumber);
    }
}
