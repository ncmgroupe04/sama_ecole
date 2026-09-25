using SamaEcole.Application.ReportCards;
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

    /// <summary>
    /// PV d'une période OU PV annuel (Évolution N°7). L'implémentation par défaut ignore la portée — elle ne sert
    /// qu'aux doublures de test antérieures ; le générateur réel la redéfinit.
    /// </summary>
    byte[] Generate(IReadOnlyList<ReportCardDto> reportCards, byte[]? schoolLogo, DeliberationScope scope)
        => Generate(reportCards, schoolLogo);
}
