using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.VieScolaire.Queries.GetParentNotice;
using MediatR;

namespace SamaEcole.Application.VieScolaire.Queries.GetParentNoticePdf;

public record GetParentNoticePdfQuery(Guid ParentSummonsId) : IRequest<ParentNoticePdfResult>, IAuditableRequest;

public record ParentNoticePdfResult(byte[] Content, string NoticeNumber);

public class GetParentNoticePdfQueryHandler(
    ISender mediator,
    IParentNoticePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetParentNoticePdfQuery, ParentNoticePdfResult>
{
    public async Task<ParentNoticePdfResult> Handle(GetParentNoticePdfQuery request, CancellationToken cancellationToken)
    {
        var notice = await mediator.Send(new GetParentNoticeQuery(request.ParentSummonsId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(notice.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={notice.NoticeNumber}");

        return new ParentNoticePdfResult(pdfGenerator.Generate(notice, logo, qrCode), notice.NoticeNumber);
    }
}
