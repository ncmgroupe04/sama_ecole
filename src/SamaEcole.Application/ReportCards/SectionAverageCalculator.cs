using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Les DEUX sections du bulletin franco-arabe. Plus grossier que <see cref="SectionType"/> à dessein :
/// le bulletin n'imprime que deux tableaux, et le pôle fin (langue arabe, sciences islamiques, matière
/// commune) ne change que la façon dont une matière se range dans l'un des deux.
///
/// • <see cref="French"/> : Français + pôle COMMUN (EPS, arts… suivis par tous les élèves) ;
/// • <see cref="Arabic"/> : Arabe + sciences islamiques.
/// </summary>
public enum ReportSection
{
    French,
    Arabic
}

public static class ReportSections
{
    public static ReportSection Of(SectionType sectionType) =>
        sectionType is SectionType.Arabic or SectionType.IslamicStudies ? ReportSection.Arabic : ReportSection.French;

    /// <summary>
    /// Section d'une matière d'après la table des pôles. Une matière ABSENTE de la table — jamais classée,
    /// ou antérieure à l'option — est française : c'est le défaut de la colonne (Subject.SectionType).
    /// </summary>
    public static ReportSection Of(Guid subjectId, IReadOnlyDictionary<Guid, SectionType> sectionTypes) =>
        sectionTypes.TryGetValue(subjectId, out var sectionType) ? Of(sectionType) : ReportSection.French;
}

/// <summary>
/// Total des coefficients et des points d'UNE section. La moyenne est dérivée, jamais stockée : elle ne peut
/// donc pas diverger des totaux imprimés à côté d'elle.
/// </summary>
public readonly record struct SectionTotals(decimal TotalCoefficients, decimal TotalPoints)
{
    /// <summary>Null tant que la section n'a aucune matière notée — jamais un zéro qui vaudrait échec.</summary>
    public decimal? Average => TotalCoefficients > 0 ? TotalPoints / TotalCoefficients : null;
}

/// <summary>
/// Les deux sections d'un élève et leur combinaison. <see cref="CombinedAverage"/> est la moyenne pondérée par
/// les coefficients de TOUTES les matières — exactement la moyenne générale du bulletin ordinaire
/// (GradeCalculator.WeightedGeneralAverage) : un bulletin franco-arabe ne peut pas annoncer une moyenne
/// générale que le même élève n'aurait pas sur le bulletin non découpé.
/// </summary>
public readonly record struct SectionBreakdown(SectionTotals French, SectionTotals Arabic)
{
    public decimal TotalCoefficients => French.TotalCoefficients + Arabic.TotalCoefficients;

    public decimal TotalPoints => French.TotalPoints + Arabic.TotalPoints;

    public decimal? CombinedAverage => TotalCoefficients > 0 ? TotalPoints / TotalCoefficients : null;

    public SectionTotals For(ReportSection section) => section == ReportSection.Arabic ? Arabic : French;
}

/// <summary>
/// Moteur de calcul du bulletin franco-arabe : moyenne de la section française, moyenne de la section arabe et
/// moyenne générale combinée. Fonctions PURES (aucun accès base, aucune horloge) : tout ce qui se calcule ici
/// est vérifiable par un test unitaire, et le même moteur sert l'élève ET chacun de ses camarades pour le rang.
///
/// POURQUOI LA COMBINAISON PAR COEFFICIENTS. Moyenne générale = (points français + points arabes) ÷ (coefficients
/// français + coefficients arabes). Une section pèse donc exactement la somme des coefficients de ses
/// matières : une école qui donne 10 de coefficients à l'arabe et 20 au français obtient un 1/3 – 2/3, sans
/// réglage supplémentaire et sans second barème à tenir cohérent avec le premier. Un ratio imposé par
/// l'établissement (indépendant des coefficients) supposerait un réglage dédié — il n'existe pas aujourd'hui.
/// </summary>
public static class SectionAverageCalculator
{
    /// <param name="subjects">
    /// Les matières NOTÉES de l'élève (<see cref="GradeSummaryDto.Subjects"/>) : <c>WeightedPoints</c> est déjà
    /// ramené au barème du bulletin, ce qui rend les sections additionnables. Les matières dispensées n'y sont
    /// pas — elles n'entrent dans aucun total.
    /// </param>
    /// <param name="sectionTypes">Pôle de chaque matière ; une matière absente de la table est française.</param>
    public static SectionBreakdown Compute(
        IEnumerable<SubjectGradeDto> subjects,
        IReadOnlyDictionary<Guid, SectionType> sectionTypes)
    {
        decimal frenchCoefficients = 0, frenchPoints = 0, arabicCoefficients = 0, arabicPoints = 0;

        foreach (var subject in subjects)
        {
            if (ReportSections.Of(subject.SubjectId, sectionTypes) == ReportSection.Arabic)
            {
                arabicCoefficients += subject.Coefficient;
                arabicPoints += subject.WeightedPoints;
            }
            else
            {
                frenchCoefficients += subject.Coefficient;
                frenchPoints += subject.WeightedPoints;
            }
        }

        return new SectionBreakdown(
            new SectionTotals(frenchCoefficients, frenchPoints),
            new SectionTotals(arabicCoefficients, arabicPoints));
    }

    /// <summary>
    /// Classement « sportif » standard : 1 + le nombre de camarades STRICTEMENT meilleurs, ex æquo au même
    /// rang (1, 2, 2, 4…). Les valeurs null — un camarade sans aucune note dans la section — n'entrent pas en
    /// compétition. Même règle que le rang général du bulletin ordinaire.
    /// </summary>
    public static int RankOf(decimal mine, IEnumerable<decimal?> classAverages) =>
        classAverages.Count(average => average.HasValue && average.Value > mine) + 1;
}
