using FluentAssertions;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>Évolution N°7 — règles du conseil de classe : note éliminatoire, propositions de décision, cohérence des seuils.</summary>
public class CouncilRulesTests
{
    private static SubjectGradeDto Subject(decimal average, decimal maxScore = 20m) =>
        new(Guid.NewGuid(), "Matière", null, null, average, null, average, 1, average, maxScore);

    [Fact]
    public void A_Subject_Average_Strictly_Below_Five_Is_Eliminatory()
    {
        CouncilRules.Default.HasEliminatoryGrade([Subject(14), Subject(5)]).Should().BeFalse();
        CouncilRules.Default.HasEliminatoryGrade([Subject(14), Subject(4.99m)]).Should().BeTrue();
    }

    [Fact]
    public void An_Eliminatory_Grade_Is_Judged_On_The_Subject_Own_Scale()
        => CouncilRules.Default.HasEliminatoryGrade([Subject(12, maxScore: 60)]).Should().BeTrue("12/60 = 4/20");

    [Theory]
    [InlineData(10.0, CouncilDecision.Admitted)]
    [InlineData(15.0, CouncilDecision.Admitted)]
    [InlineData(9.99, CouncilDecision.AllowedToRepeat)]
    [InlineData(8.5, CouncilDecision.AllowedToRepeat)]
    [InlineData(8.49, CouncilDecision.Excluded)]
    public void The_Year_End_Decision_Follows_The_Annual_Average(double average, CouncilDecision expected)
        => CouncilRules.Default.SuggestDecision((decimal)average, 20).Should().Be(expected);

    [Fact]
    public void The_Decision_Thresholds_Are_Transposed_To_The_Card_Scale()
        => CouncilRules.Default.SuggestDecision(5m, 10).Should().Be(CouncilDecision.Admitted);

    [Fact]
    public void No_Annual_Average_Means_No_Proposal()
        => CouncilRules.Default.SuggestDecision(null, 20).Should().BeNull();

    [Fact]
    public void The_Default_Rules_Are_Consistent()
        => CouncilRules.Default.Validate().Should().BeEmpty();

    [Fact]
    public void Inconsistent_Thresholds_Are_Reported()
    {
        var rules = CouncilRules.Default with { FelicitationsMin = 11, RepeatMin = 12, PromotionMin = 11, EliminatoryGrade = -1 };
        rules.Validate().Should().HaveCount(3);
    }
}
