using SamaEcole.Application.Absences.Queries.GetEntryTicket;
using SamaEcole.Application.Common.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

public class EntryTicketPdfGenerator : IEntryTicketPdfGenerator
{
    static EntryTicketPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(EntryTicketDto ticket, byte[]? logo, byte[]? surveillantSignature)
    {
        try
        {
            return new EntryTicketDocument(ticket, logo, surveillantSignature).GeneratePdf();
        }
        catch (Exception) when (logo is not null || surveillantSignature is not null)
        {
            return new EntryTicketDocument(ticket, null, null).GeneratePdf();
        }
    }
}
