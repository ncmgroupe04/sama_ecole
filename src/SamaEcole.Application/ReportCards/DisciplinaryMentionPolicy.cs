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
/// rien saisi. Toute saisie l'emporte, y compris une sanction sur un excellent bulletin — et y
/// compris <see cref="DisciplinaryMention.None"/> (« Sans distinction »), le choix explicite par
/// lequel le conseil écarte cette proposition pour un élève à 13/20 sans rien cocher d'autre. Faute
/// de cette valeur, l'absence de saisie était l'unique « rien », et valait donc « Encouragements » ;
/// <see cref="DisciplinaryMention.None"/> (stockée en chaîne, sans migration) sépare enfin « pas
/// encore décidé » de « décidé : aucune ».
/// </summary>
public static class DisciplinaryMentionPolicy
{
    /// <summary>
    /// La distinction que mérite <paramref name="generalAverage"/> selon les règles de l'école (Évolution N°7 :
    /// Félicitations, Tableau d'honneur sans note éliminatoire, Encouragements — <see cref="CouncilRules"/>), ou
    /// null s'il n'y en a aucune à proposer — jamais une sanction (voir la remarque de classe).
    ///
    /// Null aussi quand <paramref name="hasGrades"/> est faux : un bulletin sans la moindre note porte une moyenne
    /// générale de 0 par convention, et ce 0 ne dit rien de l'élève.
    /// </summary>
    public static DisciplinaryMention? Suggest(
        decimal generalAverage, int gradingScale, bool hasGrades,
        CouncilRules? rules = null, bool hasEliminatoryGrade = false)
        => (rules ?? CouncilRules.Default).SuggestDistinction(generalAverage, gradingScale, hasGrades, hasEliminatoryGrade);
}
