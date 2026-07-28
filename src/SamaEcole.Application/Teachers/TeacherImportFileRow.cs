namespace SamaEcole.Application.Teachers;

/// <summary>
/// Une ligne BRUTE d'un fichier d'import d'enseignants, avant toute validation métier — même esprit
/// que <see cref="Students.StudentImportFileRow"/> : le parseur ne fait QUE lire la structure du
/// fichier, jamais un contrôle de contenu (format de date, existence des matières...), qui reste dans
/// ImportTeachersCommandHandler.
///
/// Colonnes fixes, dans cet ORDRE (celui du modèle téléchargeable, GetTeacherImportTemplateQuery) :
/// Nom complet, Email, Téléphone, Date de naissance, Lieu de naissance, Matières — <see
/// cref="Subjects"/> porte une ou plusieurs matières séparées par une virgule, chacune au format
/// « Nom » (si le nom est sans ambiguïté dans l'établissement) ou « Nom (Niveau) » (si plusieurs
/// matières partagent ce nom à des niveaux différents — voir Domain.Entities.Subject).
/// </summary>
public record TeacherImportFileRow(
    int RowNumber,
    string FullName,
    string Email,
    string Phone,
    string BirthDate,
    string BirthPlace,
    string Subjects);
