using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.ImportGradeSheet;

/// <summary>
/// POST /grades/sheet/import — import de masse des trois épreuves (Devoir 1, Devoir 2, Composition)
/// d'une classe/matière/trimestre depuis un fichier Excel au format LARGE (une ligne par élève, une
/// colonne par épreuve — voir GradeSheetImportParser). Remplace l'ancien import à 2 colonnes + choix
/// d'épreuve par bouton radio, qui exposait exactement le risque d'inversion que ce format élimine :
/// l'élève est identifié par son matricule, l'épreuve par le NOM de sa colonne, jamais par la position.
///
/// <see cref="DryRun"/> pilote deux comportements du MÊME handler, sur le MÊME code de validation
/// (même principe qu'ImportStudentsCommand) : true (aperçu) valide tout le fichier et n'écrit RIEN ;
/// false (confirmation) re-valide intégralement puis, si tout est valide, upsert chaque note dans UNE
/// transaction. Une seule ligne invalide rejette l'import ENTIER (422, détail ligne par ligne) — jamais
/// un import partiel.
///
/// Ouvert aux mêmes rôles que la saisie unitaire (Directeur, Secrétariat, Enseignant) — c'est la même
/// action de saisie, pas une action distincte. L'import MODIFIE aussi des notes existantes : il applique
/// donc la même règle de correction que PUT /grades/{id} (GradeEditPolicy), sans quoi un Enseignant hors
/// fenêtre corrigerait par fichier ce que l'API lui refuse — 403 sur le fichier entier.
///
/// IAuditableRequest (JGK-H01) : la saisie de notes est une écriture sensible explicitement journalisée,
/// que ce soit cellule par cellule ou par lot.
/// </summary>
public record ImportGradeSheetCommand(
    Guid ClassroomId,
    Guid SubjectId,
    Guid TermId,
    bool DryRun,
    byte[] FileContent,
    string FileName)
    : IRequest<ImportGradeSheetResult>, IAuditableRequest;

/// <summary>Résumé d'un import de feuille de notes — décompte upsert, plus le nombre d'élèves reconnus par matricule.</summary>
public record ImportGradeSheetResult(int StudentsMatched, int Created, int Updated, int Unchanged);
