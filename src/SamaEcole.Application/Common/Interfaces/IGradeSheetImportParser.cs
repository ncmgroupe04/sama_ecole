namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Lit un fichier Excel d'import de notes au format LARGE (une ligne par élève, une colonne par
/// épreuve). Aucune validation MÉTIER ici (barème, appartenance à la classe...) — seulement la
/// structure du fichier : c'est ImportGradeSheetCommandHandler qui résout chaque ligne contre l'état
/// en base.
///
/// Les colonnes sont reconnues par le NOM de leur en-tête (Matricule, Devoir 1, Devoir 2, Composition),
/// jamais par leur position — l'ordre des colonnes dans le fichier n'a aucune importance, et c'est
/// précisément ce qui exclut toute inversion Devoir/Composition par erreur de mise en page.
/// </summary>
public interface IGradeSheetImportParser
{
    IReadOnlyList<GradeSheetRow> Parse(byte[] fileContent, string fileName);
}

/// <summary>
/// Une ligne brute lue de la feuille de notes, avant toute validation métier. Chaque valeur de note est
/// une chaîne vide quand la colonne existe mais que la cellule est vide (pas de note pour cette
/// épreuve) — jamais confondue avec un zéro.
/// </summary>
public record GradeSheetRow(int RowNumber, string Matricule, string Devoir1Raw, string Devoir2Raw, string CompositionRaw);
