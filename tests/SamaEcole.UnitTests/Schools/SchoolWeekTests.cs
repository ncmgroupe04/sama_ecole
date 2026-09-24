using FluentAssertions;
using SamaEcole.Application.Schools;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

public class SchoolWeekTests
{
    private static readonly DayOfWeek[] MonToFri =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    // Repos jeudi et vendredi : école franco-arabe / daara.
    private static readonly DayOfWeek[] SatToWed =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday];

    [Fact]
    public void Default_Is_Monday_To_Saturday_Like_The_Historical_Grid()
        => SchoolWeek.FromStored(SchoolWeek.DefaultStored).Should().Equal(
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Funday")]
    [InlineData("1,2,3")]
    [InlineData("Monday,Monday")]
    public void An_Empty_Or_Invalid_Stored_Value_Falls_Back_To_The_Default(string? stored)
        => SchoolWeek.FromStored(stored).Should().Equal(SchoolWeek.FromStored(SchoolWeek.DefaultStored));

    [Fact]
    public void TryParse_Accepts_Names_Case_Insensitively()
        => SchoolWeek.TryParse(["saturday", " SUNDAY ", "Monday"]).Should()
            .Equal(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday);

    [Theory]
    [InlineData("Monday", "Monday")]   // doublon
    [InlineData("Monday", "Someday")]  // inconnu
    [InlineData("Monday", "3")]        // numérique : refusé, on ne devine pas
    public void TryParse_Rejects_Duplicates_Unknown_And_Numeric_Names(string first, string second)
        => SchoolWeek.TryParse([first, second]).Should().BeNull();

    [Fact]
    public void TryParse_Rejects_An_Empty_Week()
    {
        SchoolWeek.TryParse([]).Should().BeNull();
        SchoolWeek.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void Serialize_Is_Canonical_Whatever_The_Input_Order()
        => SchoolWeek.Serialize(SatToWed).Should().Be("Monday,Tuesday,Wednesday,Saturday,Sunday");

    [Fact]
    public void Display_Order_Starts_The_Day_After_The_Rest_Block()
    {
        SchoolWeek.DisplayOrder(MonToFri).Should().Equal(MonToFri);
        SchoolWeek.DisplayOrder(SatToWed).Should().Equal(SatToWed); // Samedi → Mercredi
        SchoolWeek.DisplayOrder([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday])
            .Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);
        // Repos le vendredi seul : la semaine commence le samedi.
        SchoolWeek.DisplayOrder([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Sunday])
            .Should().Equal(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday);
    }

    [Fact]
    public void Non_Contiguous_Rest_Days_Fall_Back_To_Monday_First()
        // Repos le mercredi ET le dimanche : pas de « bloc » unique, donc pas de début de semaine évident.
        => SchoolWeek.DisplayOrder([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday])
            .Should().Equal(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday);

    [Fact]
    public void A_Full_Week_Has_No_Rest_Day()
    {
        var all = Enum.GetValues<DayOfWeek>();

        SchoolWeek.DisplayOrder(all).Should().HaveCount(7).And.StartWith(DayOfWeek.Monday);
        SchoolWeek.RestDaysLabel(all).Should().Be("aucun");
    }

    [Theory]
    [InlineData("2026-09-24", false)] // jeudi
    [InlineData("2026-09-25", false)] // vendredi
    [InlineData("2026-09-26", true)]  // samedi
    [InlineData("2026-09-27", true)]  // dimanche
    [InlineData("2026-09-23", true)]  // mercredi
    public void IsWorkingDay_Follows_The_Configured_Week(string isoDate, bool expected)
        => SchoolWeek.IsWorkingDay(SatToWed, DateOnly.Parse(isoDate)).Should().Be(expected);

    [Fact]
    public void ToNames_Returns_English_Names_In_Display_Order()
        => SchoolWeek.ToNames(SatToWed).Should().Equal("Saturday", "Sunday", "Monday", "Tuesday", "Wednesday");

    [Theory]
    [InlineData(new[] { DayOfWeek.Thursday, DayOfWeek.Friday }, "jeudi et vendredi")]
    [InlineData(new[] { DayOfWeek.Sunday }, "dimanche")]
    [InlineData(new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }, "vendredi, samedi et dimanche")]
    public void RestDaysLabel_Reads_As_French_Prose(DayOfWeek[] rest, string expected)
    {
        var working = Enum.GetValues<DayOfWeek>().Except(rest);

        SchoolWeek.RestDaysLabel(working).Should().Be(expected);
    }
}
