using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.ImportGrades;

/// <summary>
/// POST /grades/import — mode de saisie alternatif à la grille cellule par cellule : l'enseignant
/// dépose un fichier CSV/Excel à deux colonnes (matricule, note) pour UNE colonne du bulletin (Devoir
/// OU Composition) d'une classe/matière/trimestre. Réservé à l'Enseignant, même permission que
/// CreateGradeCommand (docs/Volume_7_Security.md « Notes » : Saisir = Enseignant seul) — c'est la même
/// action de saisie, pas une action distincte.
///
/// Le fichier est transporté en octets déjà lus par le contrôleur (jamais un Stream/IFormFile ici :
/// Application ne dépend pas d'ASP.NET Core), avec son nom d'origine pour que le parseur choisisse
/// entre CSV et Excel sur l'extension.
///
/// IAuditableRequest (JGK-H01) : la saisie de notes est une écriture sensible explicitement journalisée,
/// que ce soit cellule par cellule ou par lot.
/// </summary>
public record ImportGradesCommand(
    Guid ClassroomId,
    Guid SubjectId,
    Guid TermId,
    EvaluationType EvaluationType,
    byte[] FileContent,
    string FileName)
    : IRequest<ImportGradesResult>, IAuditableRequest;
