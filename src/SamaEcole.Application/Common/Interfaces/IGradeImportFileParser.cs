using SamaEcole.Application.Grades;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Lit un fichier d'import de notes (CSV ou Excel, deux colonnes : matricule puis note) et en extrait
/// les lignes BRUTES, sans validation métier — la résolution du matricule contre le roster de la classe
/// et le contrôle du barème dépendent de l'état en base et restent dans ImportGradesCommandHandler,
/// comme GradingScaleGuard pour la saisie unitaire.
///
/// Implémentation côté Infrastructure (ClosedXML pour l'Excel, Volume_2_SDS.md « bibliothèques ») :
/// Application ne référence aucune bibliothèque de troisième partie, même convention que
/// IAttendanceReportPdfGenerator.
/// </summary>
public interface IGradeImportFileParser
{
    /// <exception cref="Exceptions.ValidationException">
    /// Extension non prise en charge, fichier illisible/corrompu, ou aucune ligne de données.
    /// </exception>
    IReadOnlyList<GradeImportFileRow> Parse(byte[] fileContent, string fileName);
}
