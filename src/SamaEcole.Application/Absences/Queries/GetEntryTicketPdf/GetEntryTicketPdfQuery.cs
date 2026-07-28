using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Absences.Queries.GetEntryTicketPdf;

public record GetEntryTicketPdfQuery(Guid LateArrivalId) : IRequest<EntryTicketPdfResult>;

public record EntryTicketPdfResult(byte[] Content, string TicketNumber);

public class GetEntryTicketPdfQueryHandler(
    ISender mediator,
    IEntryTicketPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetEntryTicketPdfQuery, EntryTicketPdfResult>
{
    public async Task<EntryTicketPdfResult> Handle(GetEntryTicketPdfQuery request, CancellationToken cancellationToken)
    {
        var ticket = await mediator.Send(new GetEntryTicketQuery(request.LateArrivalId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(ticket.SchoolLogoUrl, cancellationToken);
        var surveillantSignature = await logoProvider.TryFetchAsync(ticket.SurveillantSignatureUrl, cancellationToken);

        return new EntryTicketPdfResult(pdfGenerator.Generate(ticket, logo, surveillantSignature), ticket.TicketNumber);
    }
}
