using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IClassBulletinsPdfGenerator"/> (bulletins de classe fusionnés).
/// Même garde qu'en <see cref="ReportCardPdfGenerator"/> : un logo illisible ne doit jamais empêcher
/// l'émission des bulletins — on régénère alors sans lui.
/// </summary>
public class ClassBulletinsPdfGenerator : IClassBulletinsPdfGenerator
{
    static ClassBulletinsPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null)
    {
        try
        {
            return new ClassBulletinsDocument(reportCards, logo, directorSignature, officialStamp).GeneratePdf();
        }
        catch (Exception) when (logo is not null || directorSignature is not null || officialStamp is not null)
        {
            return new ClassBulletinsDocument(reportCards, null, null, null).GeneratePdf();
        }
    }
}
