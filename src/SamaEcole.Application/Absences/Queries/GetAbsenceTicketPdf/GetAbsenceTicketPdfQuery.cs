using SamaEcole.Application.Absences.Queries.GetAbsenceTicket;
using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Absences.Queries.GetAbsenceTicketPdf;

public record GetAbsenceTicketPdfQuery(Guid AbsenceJustificationId) : IRequest<AbsenceTicketPdfResult>, IAuditableRequest;

public record AbsenceTicketPdfResult(byte[] Content, string TicketNumber);

public class GetAbsenceTicketPdfQueryHandler(
    ISender mediator,
    IAbsenceTicketPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetAbsenceTicketPdfQuery, AbsenceTicketPdfResult>
{
    public async Task<AbsenceTicketPdfResult> Handle(GetAbsenceTicketPdfQuery request, CancellationToken cancellationToken)
    {
        var ticket = await mediator.Send(new GetAbsenceTicketQuery(request.AbsenceJustificationId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(ticket.SchoolLogoUrl, cancellationToken);
        var surveillantSignature = await logoProvider.TryFetchAsync(ticket.SurveillantSignatureUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={ticket.TicketNumber}");

        return new AbsenceTicketPdfResult(pdfGenerator.Generate(ticket, logo, qrCode, surveillantSignature), ticket.TicketNumber);
    }
}
