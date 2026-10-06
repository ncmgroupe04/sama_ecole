using FluentAssertions;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>Règles pures du réglage « cycles gérés » (SchoolSettings.ManagedCycles).</summary>
public class ManagedCycleSetTests
{
    [Fact]
    public void The_Stored_Default_Lists_Every_Cycle_In_Canonical_Order()
    {
        ManagedCycleSet.AllStored.Should().Be("Maternelle,Primaire,College,Lycee");
        ManagedCycleSet.Serialize(ManagedCycleSet.All).Should().Be(ManagedCycleSet.AllStored);
        ManagedCycleSet.All.Should().BeEquivalentTo(Enum.GetValues<CycleType>(), "aucun cycle ne doit manquer à CycleType");
    }

    [Fact]
    public void The_Default_Constant_Of_School_Settings_Matches_The_Helper()
    {
        SamaEcole.Domain.Entities.SchoolSettingsDefaults.ManagedCycles.Should().Be(ManagedCycleSet.AllStored);
    }

    [Fact]
    public void Serialize_Always_Uses_The_Canonical_Order_Whatever_The_Input_Order()
    {
        ManagedCycleSet.Serialize([CycleType.Lycee, CycleType.Maternelle, CycleType.College]).Should().Be("Maternelle,College,Lycee");
        ManagedCycleSet.Serialize([CycleType.Primaire]).Should().Be("Primaire");
    }

    [Fact]
    public void Parse_Round_Trips_With_Serialize()
    {
        var parsed = ManagedCycleSet.TryParse(["Lycee", "Primaire"]);

        parsed.Should().NotBeNull();
        ManagedCycleSet.Serialize(parsed!).Should().Be("Primaire,Lycee");
        ManagedCycleSet.FromStored("Primaire,Lycee").Should().Equal(CycleType.Primaire, CycleType.Lycee);
    }

    [Theory]
    [InlineData("primaire")]
    [InlineData("  Primaire  ")]
    [InlineData("PRIMAIRE")]
    public void Parse_Ignores_Case_And_Surrounding_Spaces(string name)
    {
        ManagedCycleSet.TryParse([name]).Should().Equal(CycleType.Primaire);
    }

    [Fact]
    public void Parse_Rejects_An_Empty_List() => ManagedCycleSet.TryParse([]).Should().BeNull();

    [Fact]
    public void Parse_Rejects_Null() => ManagedCycleSet.TryParse(null).Should().BeNull();

    [Theory]
    [InlineData("Creche")]     // pas un cycle : un niveau du cycle Maternelle
    [InlineData("Collège")]    // l'enum s'écrit College
    [InlineData("Secondaire")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]          // Enum.TryParse accepterait un entier : on ne devine pas
    [InlineData("0")]
    [InlineData("99")]
    public void Parse_Rejects_Unknown_Names_Numbers_And_Blanks(string name)
    {
        ManagedCycleSet.TryParse(["Primaire", name]).Should().BeNull();
    }

    [Fact]
    public void Parse_Rejects_Duplicates()
    {
        ManagedCycleSet.TryParse(["Primaire", "primaire"]).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Inconnu")]
    [InlineData("Primaire,Primaire")]
    [InlineData("1,2")]
    public void An_Empty_Or_Unreadable_Stored_Value_Falls_Back_To_Every_Cycle_Never_To_None(string? stored)
    {
        ManagedCycleSet.FromStored(stored).Should().Equal(ManagedCycleSet.All);
    }

    [Fact]
    public void FromStored_Returns_The_Canonical_Order_Even_For_A_Hand_Edited_Value()
    {
        ManagedCycleSet.FromStored("Lycee,Maternelle").Should().Equal(CycleType.Maternelle, CycleType.Lycee);
    }

    [Fact]
    public void ToNames_Lists_Cycles_In_Canonical_Order()
    {
        ManagedCycleSet.ToNames([CycleType.College, CycleType.Maternelle]).Should().Equal("Maternelle", "College");
    }

    [Fact]
    public void Elementary_Is_Maternelle_And_Primaire_Only()
    {
        ManagedCycleSet.Elementary.Should().Equal(CycleType.Maternelle, CycleType.Primaire);
        ManagedCycleSet.Serialize(ManagedCycleSet.Elementary).Should().Be("Maternelle,Primaire");
    }

    [Fact]
    public void Removed_Lists_Only_The_Cycles_That_Disappear_In_Canonical_Order()
    {
        var removed = ManagedCycleSet.Removed(
            current: [CycleType.Maternelle, CycleType.Primaire, CycleType.College, CycleType.Lycee],
            requested: [CycleType.Primaire, CycleType.Maternelle]);

        removed.Should().Equal(CycleType.College, CycleType.Lycee);
    }

    [Fact]
    public void Removed_Is_Empty_When_Cycles_Are_Only_Added()
    {
        ManagedCycleSet.Removed([CycleType.Primaire], [CycleType.Primaire, CycleType.College]).Should().BeEmpty();
    }
}
