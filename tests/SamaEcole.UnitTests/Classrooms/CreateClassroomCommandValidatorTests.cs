using FluentAssertions;
using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

public class CreateClassroomCommandValidatorTests
{
    private readonly CreateClassroomCommandValidator _validator = new();

    private static CreateClassroomCommand Valid() => new()
    {
        Name = "CM2 A",
        Level = "Primaire",
        Capacity = 40
    };

    [Fact]
    public void Valid_Classroom_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Terminale S2")]
    [InlineData("6e B")]
    [InlineData("Grande section")]
    public void Any_Naming_Should_Be_Accepted(string name)
    {
        // La nomenclature est libre (openapi.yaml) : primaire, collège, lycée et maternelle ne
        // nomment pas leurs classes de la même façon. Toute liste figée exclurait des écoles.
        var command = Valid() with { Name = name };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Name_Should_Fail()
    {
        _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_Positive_Capacity_Should_Fail(int capacity)
    {
        _validator.Validate(Valid() with { Capacity = capacity }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Absurd_Capacity_Should_Fail()
    {
        _validator.Validate(Valid() with { Capacity = 5000 }).IsValid.Should().BeFalse();
    }
}
