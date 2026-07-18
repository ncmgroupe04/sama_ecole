namespace SamaEcole.Application.Grades;

/// <summary>
/// Une ligne brute lue d'un fichier d'import de notes (CSV/Excel), avant toute validation métier. Le
/// numéro de ligne est celui que l'enseignant verrait en ouvrant son fichier (texte : numéro de ligne ;
/// Excel : numéro de la ligne dans la feuille) — c'est ce numéro qui apparaît dans les messages
/// d'erreur renvoyés par ImportGradesCommandHandler, pour rester exploitable sans compter à la main.
/// </summary>
public record GradeImportFileRow(int RowNumber, string Matricule, string RawValue);
