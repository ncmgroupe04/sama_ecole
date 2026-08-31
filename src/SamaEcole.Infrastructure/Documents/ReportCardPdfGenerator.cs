using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Implémentation QuestPDF de <see cref="IReportCardPdfGenerator"/> (ticket JGK-G03). Le rendu passe
/// par <see cref="PdfRenderGuard"/> : un logo/cachet/signature illisible n'empêche jamais l'émission
/// du bulletin (nouvelle passe sans ornement), et un rendu vide LÈVE une exception journalisée plutôt
/// que de renvoyer un tableau de 0 octet.
/// </summary>
public class ReportCardPdfGenerator(ILogger<ReportCardPdfGenerator>? logger = null) : IReportCardPdfGenerator
{
    static ReportCardPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(ReportCardDto reportCard, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null) =>
        PdfRenderGuard.Render(
            logger,
            $"bulletin de notes (matricule {reportCard.Matricule}, {reportCard.TermLabel}, {reportCard.SchoolYearLabel})",
            () => new ReportCardDocument(reportCard, logo, directorSignature, officialStamp).GeneratePdf(),
            logo is not null || directorSignature is not null || officialStamp is not null
                ? () => new ReportCardDocument(reportCard, null, null, null).GeneratePdf()
                : null);
}
