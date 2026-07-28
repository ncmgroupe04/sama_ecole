namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère le modèle Excel téléchargeable pour l'import d'élèves (bouton « Télécharger le modèle » de
/// l'écran d'import) — colonnes dans le même ORDRE que StudentImportFileRow, avec une liste déroulante
/// Genre (M/F) et une liste déroulante Classe reprenant les VRAIES classes de l'école courante, pour
/// réduire l'erreur de frappe la plus fréquente (nom de classe mal orthographié).
///
/// Implémentation côté Infrastructure (ClosedXML), même convention que IStudentImportFileParser :
/// Application ne référence aucune bibliothèque de troisième partie.
/// </summary>
public interface IStudentImportTemplateGenerator
{
    byte[] Generate(IReadOnlyList<string> classroomNames);
}
