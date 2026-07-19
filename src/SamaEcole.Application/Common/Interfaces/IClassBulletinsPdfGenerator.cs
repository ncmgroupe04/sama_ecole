using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend les bulletins de TOUTE une classe en UN SEUL PDF (une page A5 par élève, dans l'ordre fourni) —
/// pratique pour l'impression papier en lot. Même convention que <see cref="IReportCardPdfGenerator"/> :
/// moteur QuestPDF, <paramref name="logo"/> déjà résolu en amont (E/S réseau hors du générateur).
/// </summary>
public interface IClassBulletinsPdfGenerator
{
    byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? logo);
}
