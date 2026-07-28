namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère le modèle Excel téléchargeable pour l'import d'enseignants (bouton « Télécharger le modèle »
/// de l'écran d'import) — colonnes dans le même ORDRE que TeacherImportFileRow, avec une feuille de
/// référence listant les VRAIES matières de l'école courante au format « Nom (Niveau) », pour réduire
/// l'ambiguïté d'un nom de matière partagé par plusieurs niveaux (voir Domain.Entities.Subject).
///
/// Implémentation côté Infrastructure (ClosedXML), même convention que IStudentImportTemplateGenerator.
/// </summary>
public interface ITeacherImportTemplateGenerator
{
    byte[] Generate(IReadOnlyList<(string Name, string Level)> subjects);
}
