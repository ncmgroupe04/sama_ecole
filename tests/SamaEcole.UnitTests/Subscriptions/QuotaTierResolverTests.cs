using FluentAssertions;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// Tranche MINIMALE imposée par un effectif, et suffisance d'une tranche choisie — la règle qui empêche de
/// cocher une petite tranche avec un gros effectif.
/// </summary>
public class QuotaTierResolverTests
{
    [Theory]
    [InlineData(0, StudentQuotaTier.Tier_100)]
    [InlineData(1, StudentQuotaTier.Tier_100)]
    [InlineData(100, StudentQuotaTier.Tier_100)]
    [InlineData(101, StudentQuotaTier.Tier1_150)]
    [InlineData(150, StudentQuotaTier.Tier1_150)]
    [InlineData(151, StudentQuotaTier.Tier_250)]
    [InlineData(250, StudentQuotaTier.Tier_250)]
    [InlineData(251, StudentQuotaTier.Tier2_400)]
    [InlineData(400, StudentQuotaTier.Tier2_400)]
    [InlineData(401, StudentQuotaTier.Tier3_800)]
    [InlineData(410, StudentQuotaTier.Tier3_800)]
    [InlineData(800, StudentQuotaTier.Tier3_800)]
    [InlineData(801, StudentQuotaTier.Tier4_Custom)]
    [InlineData(int.MaxValue, StudentQuotaTier.Tier4_Custom)]
    public void The_Minimum_Tier_Follows_The_Upper_Bound_Of_Each_Band(int studentCount, StudentQuotaTier expected)
    {
        QuotaTierResolver.MinimumTierFor(studentCount).Should().Be(expected);
    }

    [Fact]
    public void A_Negative_Student_Count_Is_Rejected()
    {
        var act = () => QuotaTierResolver.MinimumTierFor(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_Minimum_Tier_Always_Covers_The_Count_Within_Its_Nominal_Limit()
    {
        foreach (var count in new[] { 0, 1, 99, 100, 101, 149, 150, 151, 249, 250, 251, 399, 400, 401, 799, 800 })
        {
            var (max, _) = StudentQuotaDefaults.For(QuotaTierResolver.MinimumTierFor(count));
            max.Should().BeGreaterThanOrEqualTo(count, $"l'effectif {count} doit tenir dans le plafond nominal de sa tranche minimale");
        }
    }

    [Fact]
    public void The_Ladder_Lists_Every_Tier_Once_By_Increasing_Limit()
    {
        QuotaTierResolver.Ladder.Should().OnlyHaveUniqueItems();
        QuotaTierResolver.Ladder.Should().BeEquivalentTo(Enum.GetValues<StudentQuotaTier>(), "aucune tranche ne doit manquer à l'échelle");
        QuotaTierResolver.Ladder
            .Select(t => StudentQuotaDefaults.For(t).Max)
            .Should().BeInAscendingOrder();
    }

    [Theory]
    [InlineData(StudentQuotaTier.Tier_100, 100, true)]
    [InlineData(StudentQuotaTier.Tier_100, 101, false)]  // petite tranche cochée avec un effectif plus gros
    [InlineData(StudentQuotaTier.Tier1_150, 410, false)]
    [InlineData(StudentQuotaTier.Tier2_400, 400, true)]
    [InlineData(StudentQuotaTier.Tier2_400, 410, false)] // le cas d'école : 410 élèves sur la dernière tranche standard
    [InlineData(StudentQuotaTier.Tier3_800, 410, true)]  // une tranche supérieure reste suffisante
    [InlineData(StudentQuotaTier.Tier4_Custom, 5000, true)]
    public void A_Tier_Is_Sufficient_Only_If_It_Is_At_Least_The_Minimum_For_The_Count(
        StudentQuotaTier chosen, int declaredCount, bool sufficient)
    {
        QuotaTierResolver.IsSufficientFor(chosen, declaredCount).Should().Be(sufficient);
    }

    [Theory]
    [InlineData(StudentQuotaTier.Tier_100, false)]
    [InlineData(StudentQuotaTier.Tier1_150, false)]
    [InlineData(StudentQuotaTier.Tier_250, false)]
    [InlineData(StudentQuotaTier.Tier2_400, false)]
    [InlineData(StudentQuotaTier.Tier3_800, true)]
    [InlineData(StudentQuotaTier.Tier4_Custom, true)]
    public void Tiers_Above_The_Last_Standard_Band_Require_A_Quote(StudentQuotaTier tier, bool requiresQuote)
    {
        QuotaTierResolver.RequiresQuote(tier).Should().Be(requiresQuote);
    }
}
