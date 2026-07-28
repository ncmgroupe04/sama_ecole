using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Contrat pour la génération du Procès-Verbal (PV) de délibération d'une classe.
/// Reproduit un document récapitulatif avec les statistiques et la liste des élèves avec leurs moyennes.
/// </summary>
public interface IClassDeliberationPdfGenerator
{
    /// <summary>
    /// Génère le PDF du PV de délibération d'une classe.
    /// </summary>
    /// <param name="reportCards">La liste des bulletins de la classe (déjà calculés et classés).</param>
    /// <param name="schoolLogo">Le logo de l'établissement, s'il existe.</param>
    /// <returns>Le contenu du PDF sous forme de tableau d'octets.</returns>
    byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? schoolLogo);
}
