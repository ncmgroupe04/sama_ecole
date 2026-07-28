using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Absences.Queries.GetExitTicket;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class ExitTicketPdfGenerator : IExitTicketPdfGenerator
{
    static ExitTicketPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ExitTicketDto ticket, byte[]? logo, byte[] qrCodeImage, byte[]? surveillantSignature)
    {
        try
        {
            return new ExitTicketDocument(ticket, logo, qrCodeImage, surveillantSignature).GeneratePdf();
        }
        catch (Exception) when (logo is not null || surveillantSignature is not null)
        {
            return new ExitTicketDocument(ticket, null, qrCodeImage, null).GeneratePdf();
        }
    }
}
