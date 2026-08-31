using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IClassDeliberationPdfGenerator"/> (PV de délibération).
/// Passe par <see cref="PdfRenderGuard"/> : un logo illisible n'empêche jamais l'émission, et un rendu
/// vide LÈVE au lieu de renvoyer 0 octet.
/// </summary>
public class ClassDeliberationPdfGenerator(ILogger<ClassDeliberationPdfGenerator>? logger = null) : IClassDeliberationPdfGenerator
{
    static ClassDeliberationPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo) =>
        PdfRenderGuard.Render(
            logger,
            $"PV de délibération ({reportCards.Count} élève(s), classe {(reportCards.Count > 0 ? reportCards[0].ClassroomName : "—")}, {(reportCards.Count > 0 ? reportCards[0].TermLabel : "—")})",
            () => new ClassDeliberationDocument(reportCards, logo).GeneratePdf(),
            logo is not null ? () => new ClassDeliberationDocument(reportCards, null).GeneratePdf() : null);
}
