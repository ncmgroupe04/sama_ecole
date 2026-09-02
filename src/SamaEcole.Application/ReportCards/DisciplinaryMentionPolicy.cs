using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Pré-cochage automatique de la rangée des distinctions du bulletin (Blâme, Avertissement, Tableau
/// d'honneur, Encouragements, Félicitations) d'après la moyenne générale.
///
/// LA MOITIÉ HAUTE SEULEMENT, et c'est le cœur de cette classe. Félicitations, Tableau d'honneur et
/// Encouragements RÉCOMPENSENT un résultat : ils se déduisent d'un chiffre sans injustice, et les
/// attribuer d'office épargne au conseil un travail purement mécanique. Blâme et Avertissement
/// SANCTIONNENT un comportement : ils ne se déduisent d'aucune moyenne. Un élève faible n'est pas un
/// élève à blâmer — il peut travailler durement dans une matière qui lui résiste, traverser un deuil,
/// arriver en cours d'année. Faire prononcer une sanction disciplinaire par une division est un
/// contresens que ce fichier refuse : ces deux valeurs restent la décision du conseil des professeurs,
/// et rien ici ne les propose jamais.
///
/// La proposition ne s'impose pas : <c>ReportCardDataService</c> ne la retient que si le conseil n'a
/// rien saisi (<c>remark?.DisciplinaryMention ?? Suggest(...)</c>). Toute saisie l'emporte, y compris
/// une sanction sur un excellent bulletin.
///
/// LIMITE ASSUMÉE : le conseil ne peut plus exprimer « aucune distinction » pour un élève à 13/20 —
/// l'absence de saisie vaut désormais « Encouragements ». Le rendre possible demanderait un membre
/// <c>None</c> sur <see cref="DisciplinaryMention"/> (et sa migration), ce que l'énumération refuse
/// aujourd'hui par principe : « rien coché » y est représenté par le null, une seule fois. Décocher
/// suppose donc pour l'instant de cocher autre chose.
/// </summary>
public static class DisciplinaryMentionPolicy
{
    /// <summary>
    /// Seuils sur <see cref="MentionScales.Reference"/> (/20), plus fortes d'abord — transposés au
    /// barème du bulletin comme ceux des appréciations, pour que le primaire /10 ne soit pas jugé sur
    /// des seuils /20.
    /// </summary>
    private static readonly (DisciplinaryMention Mention, decimal MinAverage)[] OnReferenceScale =
    [
        (DisciplinaryMention.Felicitations, 16m),
        (DisciplinaryMention.TableauHonneur, 14m),
        (DisciplinaryMention.Encouragements, 12m)
    ];

    /// <summary>
    /// La distinction que mérite <paramref name="generalAverage"/>, ou null s'il n'y en a aucune à
    /// proposer — sous 12/20, aucune récompense, et jamais une sanction (voir la remarque de classe).
    ///
    /// Null aussi quand <paramref name="hasGrades"/> est faux : un bulletin sans la moindre note porte
    /// une moyenne générale de 0 par convention (<c>GradeCalculator.WeightedGeneralAverage</c> sur un
    /// ensemble vide), et ce 0 ne dit rien de l'élève. Le laisser entrer ici ne changerait aucune
    /// distinction — 0 est sous tous les seuils — mais l'oubli se paierait le jour où quelqu'un
    /// ajouterait un seuil bas.
    /// </summary>
    public static DisciplinaryMention? Suggest(decimal generalAverage, int gradingScale, bool hasGrades)
    {
        if (!hasGrades)
        {
            return null;
        }

        foreach (var (mention, minAverage) in OnReferenceScale)
        {
            if (generalAverage >= minAverage * gradingScale / MentionScales.Reference)
            {
                return mention;
            }
        }

        return null;
    }
}
