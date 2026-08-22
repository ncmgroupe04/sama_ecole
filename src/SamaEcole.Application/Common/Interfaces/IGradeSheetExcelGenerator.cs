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
    /// <summary>
    /// <paramref name="gradingScale"/> est le barème de la MATIÈRE visée — celui que l'école lui a
    /// fixé (grilles par compétences : /40, /60, /24…) ou, à défaut, celui du cycle de la classe. Il
    /// gouverne à la fois l'en-tête des colonnes et la validation Excel des cellules, qui doivent dire
    /// la même borne que celle appliquée à la réimportation.
    /// </summary>
    byte[] Generate(IReadOnlyList<GradeSheetStudentRow> rows, decimal gradingScale);
}

/// <summary>Une ligne de la feuille à générer : élève + ses notes déjà saisies (null si pas encore renseignée).</summary>
public record GradeSheetStudentRow(
    string Matricule,
    string FullName,
    decimal? Devoir1,
    decimal? Devoir2,
    decimal? Composition);
