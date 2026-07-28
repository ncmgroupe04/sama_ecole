using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletins de toute une classe fusionnés en UN SEUL document PDF, une page A5 par élève dans l'ordre
/// fourni par l'appelant — pratique pour l'impression papier en lot (bac à imprimante unique) plutôt que
/// d'ouvrir un fichier par élève. Réutilise EXACTEMENT <see cref="ReportCardDocument.ComposePage"/> pour
/// chaque élève : même mise en page que le bulletin individuel, aucune logique dupliquée.
/// </summary>
public class ClassBulletinsDocument(
    IReadOnlyList<ReportCardDto> reportCards, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = reportCards.Count > 0 ? $"Bulletins — {reportCards[0].ClassroomName}" : "Bulletins",
        Author = reportCards.Count > 0 ? reportCards[0].SchoolName : string.Empty
    };

    public void Compose(IDocumentContainer container)
    {
        foreach (var reportCard in reportCards)
        {
            new ReportCardDocument(reportCard, logo, directorSignature, officialStamp).ComposePage(container);
        }
    }
}
