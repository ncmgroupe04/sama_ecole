using FluentAssertions;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// Moteur de calcul du bulletin franco-arabe : moyenne française, moyenne arabe, moyenne générale combinée et
/// rang par section. Le point qui compte : la combinaison est PONDÉRÉE PAR LES COEFFICIENTS (jamais la moyenne
/// simple des deux moyennes), et retombe exactement sur la moyenne générale du bulletin ordinaire.
/// </summary>
public class SectionAverageCalculatorTests
{
    private static readonly Guid Maths = Guid.NewGuid();
    private static readonly Guid Eps = Guid.NewGuid();
    private static readonly Guid Fiqh = Guid.NewGuid();
    private static readonly Guid LangueArabe = Guid.NewGuid();
    private static readonly Guid SansPole = Guid.NewGuid();

    private static readonly Dictionary<Guid, SectionType> Poles = new()
    {
        [Maths] = SectionType.French,
        [Eps] = SectionType.Common,
        [Fiqh] = SectionType.IslamicStudies,
        [LangueArabe] = SectionType.Arabic
    };

    private static SubjectGradeDto Subject(Guid id, decimal average, decimal coefficient) =>
        new(id, "Matière", null, null, average, average, average, coefficient, average * coefficient);

    [Fact]
    public void French_And_Common_Subjects_Make_Up_The_French_Section()
    {
        var result = SectionAverageCalculator.Compute([Subject(Maths, 12, 4), Subject(Eps, 14, 1)], Poles);

        result.French.TotalCoefficients.Should().Be(5);
        result.French.TotalPoints.Should().Be(62);
        result.French.Average.Should().Be(12.4m);
        result.Arabic.Average.Should().BeNull("aucune matière arabe notée : pas de moyenne, jamais un zéro");
    }

    [Fact]
    public void Arabic_And_Islamic_Studies_Subjects_Make_Up_The_Arabic_Section()
    {
        var result = SectionAverageCalculator.Compute([Subject(Fiqh, 16, 2), Subject(LangueArabe, 8, 3)], Poles);

        result.Arabic.TotalCoefficients.Should().Be(5);
        result.Arabic.TotalPoints.Should().Be(56);
        result.Arabic.Average.Should().Be(11.2m);
        result.French.Average.Should().BeNull();
    }

    [Fact]
    public void A_Subject_Missing_From_The_Pole_Table_Is_French()
    {
        // Matière jamais classée, ou antérieure à l'option : c'est le défaut de la colonne (Subject.SectionType).
        var result = SectionAverageCalculator.Compute([Subject(SansPole, 10, 2)], Poles);

        result.French.TotalCoefficients.Should().Be(2);
        result.Arabic.TotalCoefficients.Should().Be(0);
    }

    [Fact]
    public void The_Combined_Average_Is_Weighted_By_Coefficients_Not_A_Simple_Mean_Of_The_Two_Averages()
    {
        // Français : 10 sur 10 points de coefficient ; arabe : 16 sur 20. Moyenne simple des moyennes = 13 ;
        // pondérée = (10×10 + 16×20) ÷ 30 = 14. C'est la seconde que le bulletin doit annoncer.
        var result = SectionAverageCalculator.Compute([Subject(Maths, 10, 10), Subject(Fiqh, 16, 20)], Poles);

        result.French.Average.Should().Be(10m);
        result.Arabic.Average.Should().Be(16m);
        result.CombinedAverage.Should().BeApproximately(14m, 0.0001m);
    }

    [Fact]
    public void The_Combined_Average_Equals_The_Ordinary_General_Average_Of_The_Report_Card()
    {
        SubjectGradeDto[] subjects =
        [
            Subject(Maths, 12, 4), Subject(Eps, 14, 1), Subject(Fiqh, 16, 2), Subject(LangueArabe, 8, 3)
        ];

        var combined = SectionAverageCalculator.Compute(subjects, Poles);
        var general = GradeCalculator.WeightedGeneralAverage(subjects.Select(s => ((decimal?)s.Average, s.Coefficient)));

        combined.CombinedAverage.Should().Be(general.GeneralAverage,
            "un bulletin franco-arabe ne peut pas annoncer une moyenne générale que le bulletin non découpé contredirait");
        combined.TotalCoefficients.Should().Be(general.TotalCoefficients);
        combined.TotalPoints.Should().Be(general.TotalPoints);
    }

    [Fact]
    public void A_Student_Without_Any_Graded_Subject_Has_No_Average_At_All()
    {
        var result = SectionAverageCalculator.Compute([], Poles);

        result.French.Average.Should().BeNull();
        result.Arabic.Average.Should().BeNull();
        result.CombinedAverage.Should().BeNull();
    }

    [Fact]
    public void A_Rebased_Primary_Grade_Counts_Through_Its_Weighted_Points()
    {
        // WeightedPoints est DÉJÀ ramené au barème du bulletin (ligne /40 → /20) : le calcul s'appuie dessus,
        // jamais sur la note brute — sans quoi une ligne /40 pèserait le double.
        var rebased = new SubjectGradeDto(Fiqh, "Fiqh", null, null, 30, 30, 30, 1, WeightedPoints: 15, MaxScore: 40);

        var result = SectionAverageCalculator.Compute([rebased], Poles);

        result.Arabic.TotalPoints.Should().Be(15);
        result.Arabic.Average.Should().Be(15m);
    }

    [Theory]
    [InlineData(SectionType.French, ReportSection.French)]
    [InlineData(SectionType.Common, ReportSection.French)]
    [InlineData(SectionType.Arabic, ReportSection.Arabic)]
    [InlineData(SectionType.IslamicStudies, ReportSection.Arabic)]
    public void Every_Pole_Maps_To_Exactly_One_Report_Section(SectionType pole, ReportSection expected)
    {
        ReportSections.Of(pole).Should().Be(expected);
    }

    [Fact]
    public void Every_Pole_Value_Is_Mapped_So_A_New_Pole_Cannot_Silently_Fall_Into_The_French_Section()
    {
        // Garde-fou : ajouter une valeur à SectionType sans décider de sa section fait échouer ce test, au lieu
        // de ranger la matière en français sans que personne ne l'ait voulu.
        var arabic = Enum.GetValues<SectionType>().Where(p => ReportSections.Of(p) == ReportSection.Arabic);

        arabic.Should().BeEquivalentTo([SectionType.Arabic, SectionType.IslamicStudies]);
        Enum.GetValues<SectionType>().Should().HaveCount(4);
    }

    [Fact]
    public void Rank_Is_One_Plus_The_Number_Of_Strictly_Better_Classmates()
    {
        SectionAverageCalculator.RankOf(12, [16m, 12m, 8m]).Should().Be(2);
    }

    [Fact]
    public void Tied_Students_Share_A_Rank_And_The_Next_One_Skips()
    {
        decimal?[] averages = [16m, 13m, 13m, 9m];

        SectionAverageCalculator.RankOf(16, averages).Should().Be(1);
        SectionAverageCalculator.RankOf(13, averages).Should().Be(2);
        SectionAverageCalculator.RankOf(9, averages).Should().Be(4, "deux élèves devant à égalité : le suivant est 4e, pas 3e");
    }

    [Fact]
    public void A_Classmate_Without_A_Section_Average_Does_Not_Compete()
    {
        SectionAverageCalculator.RankOf(10, [null, null, 12m]).Should().Be(2);
    }
}
