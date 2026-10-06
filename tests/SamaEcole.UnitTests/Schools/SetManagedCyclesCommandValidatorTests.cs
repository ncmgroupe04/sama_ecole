using FluentAssertions;
using SamaEcole.Application.Schools.Commands.SetManagedCycles;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>Validation de la demande « cycles gérés » : au moins un cycle connu, sans doublon.</summary>
public class SetManagedCyclesCommandValidatorTests
{
    private readonly SetManagedCyclesCommandValidator _validator = new();

    [Theory]
    [InlineData("Primaire")]
    [InlineData("Maternelle", "Primaire")]
    [InlineData("Maternelle", "Primaire", "College", "Lycee")]
    [InlineData("lycee", "COLLEGE")]
    public void A_Non_Empty_List_Of_Known_Distinct_Cycles_Is_Valid(params string[] cycles)
    {
        _validator.Validate(new SetManagedCyclesCommand(cycles)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_List_Is_Refused()
    {
        _validator.Validate(new SetManagedCyclesCommand([])).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Creche")]
    [InlineData("Collège")]
    [InlineData("Secondaire")]
    [InlineData("")]
    [InlineData("2")]
    public void An_Unknown_Name_A_Number_Or_A_Blank_Is_Refused(string name)
    {
        _validator.Validate(new SetManagedCyclesCommand(["Primaire", name])).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Duplicate_Is_Refused()
    {
        _validator.Validate(new SetManagedCyclesCommand(["Primaire", "Primaire"])).IsValid.Should().BeFalse();
    }
}
