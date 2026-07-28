using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rend un bulletin de notes en PDF (ticket JGK-G03). Même convention que
/// <see cref="IPaymentReceiptPdfGenerator"/> : moteur QuestPDF, référence de design
/// (docs/design-references/bulletin-reference.png), <paramref name="logo"/>/<paramref name="directorSignature"/>/
/// <paramref name="officialStamp"/> déjà résolus en amont (E/S réseau hors du générateur, qui reste pur
/// et synchrone).
/// </summary>
public interface IReportCardPdfGenerator
{
    byte[] Generate(ReportCardDto reportCard, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null);
}
