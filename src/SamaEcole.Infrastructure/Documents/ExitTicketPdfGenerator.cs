using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Absences.Queries.GetExitTicket;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExitTicketPdfGenerator(ILogger<ExitTicketPdfGenerator>? logger = null) : IExitTicketPdfGenerator
{
    static ExitTicketPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExitTicketDto ticket, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature) =>
        PdfRenderGuard.Render(
            logger,
            $"billet de sortie {ticket.TicketNumber} (matricule {ticket.Matricule}, sortie {ticket.EarlyDepartureId})",
            () => new ExitTicketDocument(ticket, logo, qrCodeImage, surveillantSignature).GeneratePdf(),
            logo is not null || surveillantSignature is not null
                ? () => new ExitTicketDocument(ticket, null, qrCodeImage, null).GeneratePdf()
                : null);
}
