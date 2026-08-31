using Microsoft.Extensions.Logging;
using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using SamaEcole.Application.Common.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class EntryTicketPdfGenerator(ILogger<EntryTicketPdfGenerator>? logger = null) : IEntryTicketPdfGenerator
{
    static EntryTicketPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EntryTicketDto ticket, byte[]? logo, byte[]? surveillantSignature) =>
        PdfRenderGuard.Render(
            logger,
            $"billet d'entrée {ticket.TicketNumber} (matricule {ticket.Matricule}, retard {ticket.LateArrivalId})",
            () => new EntryTicketDocument(ticket, logo, surveillantSignature).GeneratePdf(),
            logo is not null || surveillantSignature is not null
                ? () => new EntryTicketDocument(ticket, null, null).GeneratePdf()
                : null);
}
