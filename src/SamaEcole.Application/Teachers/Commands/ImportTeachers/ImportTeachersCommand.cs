using MediatR;

namespace SamaEcole.Application.Teachers.Commands.ImportTeachers;

/// <summary>
/// POST /api/v1/teachers/import — import de masse du corps professoral (fichier CSV/Excel, un
/// enseignant par ligne — voir TeacherImportFileRow). Même patron de référence que
/// Students/ImportStudents pour la génération du matricule (AGENTS.md règle #3) : chaque matricule
/// n'est produit que DANS la transaction du commit final, jamais pendant l'aperçu.
///
/// <see cref="DryRun"/> pilote DEUX comportements du MÊME handler, sur le MÊME code de validation —
/// jamais deux implémentations qui pourraient diverger :
///   * true  (aperçu) — valide chaque ligne, n'écrit RIEN, renvoie 200 avec le détail ligne par ligne
///     (valeurs brutes + erreurs par champ) pour que l'écran affiche les cellules en rouge AVANT toute
///     insertion.
///   * false (confirmation) — RE-valide intégralement (défense en profondeur : un enseignant a pu être
///     créé entre l'aperçu et la confirmation, rendant un e-mail/téléphone désormais en doublon) ; si
///     la moindre ligne est invalide, RIEN n'est écrit (ValidationException, 422, une entrée par ligne
///     en erreur) — jamais un import partiel. Si tout est valide, chaque enseignant est créé dans UNE
///     seule transaction.
/// </summary>
public record ImportTeachersCommand(byte[] FileContent, string FileName, bool DryRun) : IRequest<ImportTeachersResult>;

/// <summary>
/// Résultat d'UNE ligne, valeurs brutes échangées ET erreurs PAR CHAMP (pas juste « ligne invalide ») :
/// l'écran d'aperçu peut ainsi colorer précisément la ou les cellules fautives. Clés de
/// <see cref="FieldErrors"/> : "fullName", "email", "phone", "birthDate", "birthPlace", "subjects".
/// </summary>
public record ImportTeachersRowResult(
    int RowNumber,
    bool IsValid,
    string FullName,
    string Email,
    string Phone,
    string BirthDate,
    string BirthPlace,
    string Subjects,
    IReadOnlyDictionary<string, string> FieldErrors);

public record ImportTeachersResult(
    bool DryRun,
    bool Committed,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    int Created,
    IReadOnlyList<ImportTeachersRowResult> Rows);
