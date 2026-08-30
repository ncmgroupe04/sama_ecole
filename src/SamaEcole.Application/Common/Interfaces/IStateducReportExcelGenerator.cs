using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Rapport STATEDUC en classeur .xlsx — une feuille par tableau réglementaire (Effectifs, Pyramide
/// des âges, Qualifications, Statuts, Synthèse).
///
/// Le PDF et le classeur ne se remplacent pas : le PDF est la pièce SIGNÉE que l'école dépose, le
/// classeur est ce que l'agent de l'IEF recopie ou consolide. C'est pourquoi les effectifs y sont des
/// NOMBRES au format Excel, jamais des chaînes déjà mises en forme — même règle que
/// <see cref="IRevenueReportExcelGenerator"/> : un agent doit pouvoir sommer et croiser ces colonnes,
/// ce qu'un « 214 élèves » textuel interdirait.
///
/// Implémentation côté Infrastructure (ClosedXML).
/// </summary>
public interface IStateducReportExcelGenerator
{
    byte[] Generate(StateducReportDto report);
}
