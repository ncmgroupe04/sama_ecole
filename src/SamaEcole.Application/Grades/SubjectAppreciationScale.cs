using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Barème des APPRÉCIATIONS PAR MATIÈRE du bulletin — la dernière colonne de
/// docs/design-references/bulletin-reference.png (« Bon Travail », « A. Bien », « Moyen »,
/// « Insuffisant », « Faible ») — et le seuil du « T.H » qui la précède de deux colonnes.
///
/// DÉLIBÉRÉMENT SÉPARÉ de l'échelle de mentions de l'école (<see cref="Mention"/>, résolue par
/// <c>MentionScale</c>) : les deux qualifient des choses différentes, avec des mots différents. La
/// MENTION qualifie la moyenne GÉNÉRALE et reste configurable par le Directeur (Excellent, Très Bien,
/// Bien, Assez Bien, Passable) ; l'APPRÉCIATION qualifie UNE matière et suit le vocabulaire fixe des
/// bulletins sénégalais. Le bulletin réutilisait jusqu'ici l'échelle de mentions pour les deux — au nom
/// de « une seule source de vérité, aucun vocabulaire parallèle » — et imprimait donc « Passable » là
/// où la référence porte « Faible », et rien du tout sous le plus bas seuil de l'école. Ce n'était pas
/// un vocabulaire parallèle de trop : c'était le mauvais vocabulaire, appliqué au mauvais objet.
///
/// Les seuils sont fixes et NON configurables, contrairement aux mentions : ils font partie du gabarit
/// du document officiel que la règle #12 d'AGENTS.md impose de reproduire, au même titre que les
/// intitulés de colonnes. Une école qui voudrait les siens changerait la forme du bulletin, pas un
/// réglage.
///
/// Seuils exprimés sur <see cref="MentionScales.Reference"/> (/20), comme ceux des mentions, puis
/// transposés au barème du bulletin via <see cref="MentionScales.RescaleTo"/> : sans quoi un bulletin
/// primaire /10 jugerait ses moyennes sur des seuils /20 et n'imprimerait jamais mieux que « Moyen ».
/// </summary>
public static class SubjectAppreciationScale
{
    /// <summary>
    /// Le barème de la référence, plus fortes d'abord — l'ordre décroissant qu'attend
    /// <see cref="GradeCalculator.MentionFor"/>, qui retient le premier seuil atteint.
    ///
    /// « Faible » a un seuil de ZÉRO, et non le seuil 8 de sa borne haute : c'est le plancher du barème,
    /// donc toute matière notée reçoit une appréciation. Là où <see cref="GradeCalculator.MentionFor"/>
    /// rend null sous le plus bas seuil — « cette moyenne ne mérite aucune mention » —, une matière
    /// notée 3/20 n'est pas une matière sans appréciation : elle est faible, et le bulletin le dit.
    /// </summary>
    private static readonly (string Label, decimal MinAverage)[] OnReferenceScale =
    [
        ("Très Bien", 16m),
        ("Bon Travail", 14m),
        ("Assez Bien", 12m),
        ("Moyen", 10m),
        ("Insuffisant", 8m),
        ("Faible", 0m)
    ];

    /// <summary>
    /// Seuil du Tableau d'Honneur par matière (colonne « T.H »), sur <see cref="MentionScales.Reference"/>.
    ///
    /// C'est EXACTEMENT le seuil de « Bon Travail » ci-dessus, et ce n'est pas une coïncidence : une
    /// matière décrochant le T.H est une matière dont l'appréciation atteint « Bon Travail ». Les deux
    /// colonnes disent la même chose sous deux formes, elles ne peuvent pas se contredire — d'où une
    /// seule constante, référencée par le barème, plutôt que deux 14 indépendants qui dériveraient au
    /// premier ajustement.
    /// </summary>
    public const decimal HonorMinAverage = 14m;

    /// <summary>Le barème transposé à celui d'un bulletin donné (20 au secondaire, 10 au primaire).</summary>
    public static IReadOnlyList<(string Label, decimal MinAverage)> ForScale(int gradingScale) =>
        MentionScales.RescaleTo(OnReferenceScale, gradingScale);

    /// <summary>
    /// L'appréciation d'une moyenne de matière exprimée sur <paramref name="gradingScale"/>. Jamais
    /// null pour une moyenne positive — voir le plancher « Faible » ci-dessus.
    /// </summary>
    public static string? For(decimal average, int gradingScale) =>
        GradeCalculator.MentionFor(average, ForScale(gradingScale));

    /// <summary>
    /// La matière décroche-t-elle le Tableau d'Honneur ? Le seuil est transposé au barème du bulletin
    /// exactement comme les appréciations : 14/20 au secondaire, son équivalent 7/10 au primaire.
    /// </summary>
    public static bool QualifiesForHonors(decimal average, int gradingScale) =>
        average >= HonorMinAverage * gradingScale / MentionScales.Reference;
}
