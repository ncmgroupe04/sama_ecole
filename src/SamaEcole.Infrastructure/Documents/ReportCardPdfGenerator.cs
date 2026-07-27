using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IReportCardPdfGenerator"/> (ticket JGK-G03). Même garde qu'en
/// PaymentReceiptPdfGenerator : un logo illisible ne doit jamais empêcher l'émission du bulletin — on
/// régénère alors sans lui.
/// </summary>
public class ReportCardPdfGenerator : IReportCardPdfGenerator
{
    static ReportCardPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ReportCardDto reportCard, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null)
    {
        try
        {
            return new ReportCardDocument(reportCard, logo, directorSignature, officialStamp).GeneratePdf();
        }
        catch (Exception) when (logo is not null || directorSignature is not null || officialStamp is not null)
        {
            return new ReportCardDocument(reportCard, null, null, null).GeneratePdf();
        }
    }
}
