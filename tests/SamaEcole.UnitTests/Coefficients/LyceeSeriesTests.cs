using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

public class LyceeSeriesTests
{
    [Fact]
    public void The_Catalogue_Holds_The_Five_Agreed_Series()
        => LyceeSeries.All.Select(s => s.Code).Should().Equal("L1", "L2", "S1", "S2", "TECH");

    [Theory]
    [InlineData("S2", true)]
    [InlineData("s2", true)]      // Normalize d'abord : la casse ne fait pas une autre série
    [InlineData(" tech ", true)]
    [InlineData("S3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_Follows_The_Catalogue(string? code, bool expected)
        => LyceeSeries.IsValid(LyceeSeries.Normalize(code)).Should().Be(expected);

    [Fact]
    public void Normalize_Trims_And_Uppercases_And_Maps_Blank_To_Null()
    {
        LyceeSeries.Normalize(" s1 ").Should().Be("S1");
        LyceeSeries.Normalize("   ").Should().BeNull();
    }
}
