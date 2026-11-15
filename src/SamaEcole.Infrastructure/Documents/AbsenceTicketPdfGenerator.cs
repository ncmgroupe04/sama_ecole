using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Absences.Queries.GetAbsenceTicket;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class AbsenceTicketPdfGenerator(ILogger<AbsenceTicketPdfGenerator>? logger = null) : IAbsenceTicketPdfGenerator
{
    static AbsenceTicketPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(AbsenceTicketDto ticket, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature) =>
        PdfRenderGuard.Render(
            logger,
            $"billet d'absence {ticket.TicketNumber} (matricule {ticket.Matricule}, absence {ticket.AbsenceJustificationId})",
            () => new AbsenceTicketDocument(ticket, logo, qrCodeImage, surveillantSignature).GeneratePdf(),
            logo is not null || surveillantSignature is not null
                ? () => new AbsenceTicketDocument(ticket, null, qrCodeImage, null).GeneratePdf()
                : null);
}
