using MediatR;

namespace SamaEcole.Application.Students.Commands.ImportStudents;

/// <summary>
/// POST /api/v1/students/import — import de masse pour la rentrée scolaire (fichier CSV/Excel, une
/// classe par ligne — voir StudentImportFileRow). Même patron de référence que Students/Enrollments
/// pour la génération du matricule (AGENTS.md règle #3) : chaque matricule n'est produit que DANS la
/// transaction du commit final, jamais pendant l'aperçu.
///
/// <see cref="DryRun"/> pilote DEUX comportements du MÊME handler, sur le MÊME code de validation —
/// jamais deux implémentations qui pourraient diverger :
///   * true  (aperçu) — valide chaque ligne, n'écrit RIEN, renvoie 200 avec le détail ligne par ligne
///     (valeurs brutes + erreurs par champ) pour que l'écran affiche les cellules en rouge AVANT toute
///     insertion. Un fichier volumineux (rentrée scolaire) peut ainsi être corrigé sans jamais risquer
///     un import à moitié appliqué si la connexion venait à sauter en cours de route.
///   * false (confirmation) — RE-valide intégralement (défense en profondeur : la classe visée par une
///     ligne a pu être supprimée entre l'aperçu et la confirmation) ; si la moindre ligne est invalide,
///     RIEN n'est écrit (ValidationException, 422, une entrée par ligne en erreur) — jamais un import
///     partiel. Si tout est valide, chaque élève est créé dans UNE seule transaction.
/// </summary>
public record ImportStudentsCommand(byte[] FileContent, string FileName, bool DryRun) : IRequest<ImportStudentsResult>;

/// <summary>
/// Résultat d'UNE ligne, valeurs brutes échangées ET erreurs PAR CHAMP (pas juste « ligne invalide ») :
/// l'écran d'aperçu peut ainsi colorer précisément la ou les cellules fautives, comme demandé pour la
/// rentrée scolaire — une date mal formée n'empêche pas de voir que le lieu de naissance, lui, est bon.
/// Clés de <see cref="FieldErrors"/> : "fullName", "birthDate", "birthPlace", "gender", "classroomName",
/// "guardianName", "guardianPhone", "guardianEmail", "address".
/// </summary>
public record ImportStudentsRowResult(
    int RowNumber,
    bool IsValid,
    string FullName,
    string BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string GuardianName,
    string GuardianPhone,
    string GuardianEmail,
    string Address,
    IReadOnlyDictionary<string, string> FieldErrors);

public record ImportStudentsResult(
    bool DryRun,
    bool Committed,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int Created,
    IReadOnlyList<ImportStudentsRowResult> Rows);
