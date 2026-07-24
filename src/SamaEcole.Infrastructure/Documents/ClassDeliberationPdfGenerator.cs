using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IClassDeliberationPdfGenerator"/> (PV de délibération).
/// Même garde qu'en <see cref="ReportCardPdfGenerator"/> : un logo illisible ne doit jamais empêcher
/// l'émission du document — on régénère alors sans lui.
/// </summary>
public class ClassDeliberationPdfGenerator : IClassDeliberationPdfGenerator
{
    static ClassDeliberationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo)
    {
        try
        {
            return new ClassDeliberationDocument(reportCards, logo).GeneratePdf();
        }
        catch (Exception) when (logo is not null)
        {
            return new ClassDeliberationDocument(reportCards, null).GeneratePdf();
        }
    }
}
