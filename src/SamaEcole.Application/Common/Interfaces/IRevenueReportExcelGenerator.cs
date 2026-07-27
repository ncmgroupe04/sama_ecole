using SamaEcole.Application.Finance.Queries.GetRevenueConsolidation;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Export comptable (.xlsx) de la consolidation des revenus — une feuille par axe d'analyse
/// (Cycle, Classe, Mode de paiement), plus une synthèse.
///
/// Implémentation côté Infrastructure (ClosedXML), même convention qu'
/// <see cref="IGradeSheetExcelGenerator"/> : Application ne référence aucune bibliothèque tierce.
/// </summary>
public interface IRevenueReportExcelGenerator
{
    byte[] Generate(RevenueConsolidationDto report, string schoolName);
}
