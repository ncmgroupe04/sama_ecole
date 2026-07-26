using FluentAssertions;
using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using Xunit;

namespace SamaEcole.UnitTests.Buildings;

public class CreateBuildingCommandValidatorTests
{
    private readonly CreateBuildingCommandValidator _validator = new();

    private static CreateBuildingCommand Valid() => new()
    {
        Name = "Bâtiment A",
        Description = "Bâtiment principal, 2 étages"
    };

    [Fact]
    public void Valid_Building_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void No_Description_Should_Pass()
    {
        _validator.Validate(Valid() with { Description = null }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Name_Should_Fail()
    {
        _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Name_Too_Long_Should_Fail()
    {
        _validator.Validate(Valid() with { Name = new string('A', 101) }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    public void Unsafe_Name_Should_Fail(string name)
    {
        _validator.Validate(Valid() with { Name = name }).IsValid.Should().BeFalse();
    }
}
