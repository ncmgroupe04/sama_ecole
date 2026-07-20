namespace SamaEcole.Application.Students;

/// <summary>
/// Une ligne BRUTE d'un fichier d'import d'élèves (ticket import de masse), avant toute validation
/// métier — même esprit que <see cref="Grades.GradeImportFileRow"/> : le parseur ne fait QUE lire la
/// structure du fichier, jamais un contrôle de contenu (format de date, existence de la classe...), qui
/// reste dans ImportStudentsCommandHandler.
///
/// Colonnes fixes, dans cet ORDRE (celui du modèle téléchargeable, GetStudentImportTemplateQuery) :
/// Nom complet, Date de naissance, Lieu de naissance, Genre, Classe, Nom du tuteur, Téléphone du
/// tuteur — une CLASSE PAR LIGNE (pas un import ciblé sur une seule classe comme les notes) : une
/// rentrée scolaire mélange plusieurs classes dans un même fichier.
/// </summary>
public record StudentImportFileRow(
    int RowNumber,
    string FullName,
    string BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string GuardianName,
    string GuardianPhone);
