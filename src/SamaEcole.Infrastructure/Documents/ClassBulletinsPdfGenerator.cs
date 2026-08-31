using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IClassBulletinsPdfGenerator"/> (bulletins de classe fusionnés).
/// Passe par <see cref="PdfRenderGuard"/> : un logo illisible n'empêche jamais l'émission, et un rendu
/// vide LÈVE au lieu de renvoyer 0 octet.
/// </summary>
public class ClassBulletinsPdfGenerator(ILogger<ClassBulletinsPdfGenerator>? logger = null) : IClassBulletinsPdfGenerator
{
    static ClassBulletinsPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null) =>
        PdfRenderGuard.Render(
            logger,
            $"bulletins fusionnés ({reportCards.Count} élève(s), classe {(reportCards.Count > 0 ? reportCards[0].ClassroomName : "—")}, {(reportCards.Count > 0 ? reportCards[0].TermLabel : "—")})",
            () => new ClassBulletinsDocument(reportCards, logo, directorSignature, officialStamp).GeneratePdf(),
            logo is not null || directorSignature is not null || officialStamp is not null
                ? () => new ClassBulletinsDocument(reportCards, null, null, null).GeneratePdf()
                : null);
}
