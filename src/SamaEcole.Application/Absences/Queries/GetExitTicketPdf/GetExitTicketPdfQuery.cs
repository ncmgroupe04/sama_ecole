using SamaEcole.Application.Absences.Queries.GetExitTicket;
using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Absences.Queries.GetExitTicketPdf;

public record GetExitTicketPdfQuery(Guid EarlyDepartureId) : IRequest<ExitTicketPdfResult>, IAuditableRequest;

public record ExitTicketPdfResult(byte[] Content, string TicketNumber);

public class GetExitTicketPdfQueryHandler(
    ISender mediator,
    IExitTicketPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetExitTicketPdfQuery, ExitTicketPdfResult>
{
    public async Task<ExitTicketPdfResult> Handle(GetExitTicketPdfQuery request, CancellationToken cancellationToken)
    {
        var ticket = await mediator.Send(new GetExitTicketQuery(request.EarlyDepartureId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(ticket.SchoolLogoUrl, cancellationToken);
        var surveillantSignature = await logoProvider.TryFetchAsync(ticket.SurveillantSignatureUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={ticket.TicketNumber}");

        return new ExitTicketPdfResult(pdfGenerator.Generate(ticket, logo, qrCode, surveillantSignature), ticket.TicketNumber);
    }
}
