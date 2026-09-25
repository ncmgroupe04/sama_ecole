using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

public class LyceeSeriesTests
{
    [Fact]
    public void The_Catalogue_Holds_The_Baccalaureate_Series_Then_The_Legacy_Codes()
        => LyceeSeries.All.Select(s => s.Code).Should().Equal(
            "L1A", "L1B", "L'1", "L2", "S1", "S2", "S3", "S4", "S5", "STEG", "T1", "T2", "STIDD", "LA", "S1A", "S2A",
            "L1", "TECH");

    [Fact]
    public void Only_The_Codes_Of_The_Former_Nomenclature_Are_Legacy()
        => LyceeSeries.All.Where(s => s.IsLegacy).Select(s => s.Code).Should().Equal("L1", "TECH");

    [Fact]
    public void Every_Series_Belongs_To_One_Of_The_Four_Families()
        => LyceeSeries.All.Select(s => s.Category).Distinct().Should().BeEquivalentTo(
            LyceeSeries.Literary, LyceeSeries.Scientific, LyceeSeries.Technical, LyceeSeries.FrancoArabic);

    [Fact]
    public void Codes_Fit_The_Database_Column()
        => LyceeSeries.All.Should().OnlyContain(s => s.Code.Length <= 10, "Classroom.Series est un varchar(10)");

    [Theory]
    [InlineData("S2", true)]
    [InlineData("s2", true)]      // Normalize d'abord : la casse ne fait pas une autre série
    [InlineData(" tech ", true)]  // ancien code : toujours valide
    [InlineData("l1a", true)]
    [InlineData("L’1", true)]     // apostrophe typographique
    [InlineData("stidd", true)]
    [InlineData("S2A", true)]
    [InlineData("S6", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_Follows_The_Catalogue(string? code, bool expected)
        => LyceeSeries.IsValid(LyceeSeries.Normalize(code)).Should().Be(expected);

    [Fact]
    public void Normalize_Trims_And_Uppercases_And_Maps_Blank_To_Null()
    {
        LyceeSeries.Normalize(" s1 ").Should().Be("S1");
        LyceeSeries.Normalize("l‘1").Should().Be("L'1");
        LyceeSeries.Normalize("   ").Should().BeNull();
    }
}
