namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Génère la feuille de notes Excel téléchargeable pour une classe/matière/trimestre (export sécurisé,
/// ticket import/export Excel des notes) : colonnes Matricule, Nom &amp; Prénom, Devoir 1, Devoir 2,
/// Composition, matricules/notes déjà saisies pré-remplis, colonnes d'identification verrouillées.
///
/// Implémentation côté Infrastructure (ClosedXML), même convention que
/// <see cref="IStudentImportTemplateGenerator"/> : Application ne référence aucune bibliothèque tierce.
/// </summary>
public interface IGradeSheetExcelGenerator
{
    byte[] Generate(IReadOnlyList<GradeSheetStudentRow> rows, int gradingScale);
}

/// <summary>Une ligne de la feuille à générer : élève + ses notes déjà saisies (null si pas encore renseignée).</summary>
public record GradeSheetStudentRow(
    string Matricule,
    string FullName,
    decimal? Devoir1,
    decimal? Devoir2,
    decimal? Composition);
