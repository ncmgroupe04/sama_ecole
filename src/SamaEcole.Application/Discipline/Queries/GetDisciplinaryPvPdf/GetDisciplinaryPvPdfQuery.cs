using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;
using MediatR;

namespace SamaEcole.Application.Discipline.Queries.GetDisciplinaryPvPdf;

public record GetDisciplinaryPvPdfQuery(Guid DisciplineRecordId) : IRequest<DisciplinaryPvPdfResult>, IAuditableRequest;

public record DisciplinaryPvPdfResult(byte[] Content, string PvNumber);

public class GetDisciplinaryPvPdfQueryHandler(
    ISender mediator,
    IDisciplinaryPvPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetDisciplinaryPvPdfQuery, DisciplinaryPvPdfResult>
{
    public async Task<DisciplinaryPvPdfResult> Handle(GetDisciplinaryPvPdfQuery request, CancellationToken cancellationToken)
    {
        var pv = await mediator.Send(new GetDisciplinaryPvQuery(request.DisciplineRecordId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(pv.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={pv.PvNumber}");

        return new DisciplinaryPvPdfResult(pdfGenerator.Generate(pv, logo, qrCode), pv.PvNumber);
    }
}
