using FluentAssertions;
using SamaEcole.Application.Subjects.Commands.CreateSubject;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subjects;

public class CreateSubjectCommandValidatorTests
{
    private readonly CreateSubjectCommandValidator _validator = new();

    private static CreateSubjectCommand Valid() => new()
    {
        Name = "Mathématiques",
        Level = "Primaire",
        Coefficient = 4
    };

    [Fact]
    public void Valid_Subject_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(6)]
    public void Reasonable_Coefficients_Should_Be_Accepted(double coefficient)
    {
        // Un coefficient décimal est légitime (1,5 existe dans certains barèmes).
        _validator.Validate(Valid() with { Coefficient = (decimal)coefficient }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SectionType.French)]
    [InlineData(SectionType.Arabic)]
    [InlineData(SectionType.IslamicStudies)]
    [InlineData(SectionType.Common)]
    public void Every_Pole_Or_None_Should_Be_Accepted(SectionType? pole)
    {
        _validator.Validate(Valid() with { SectionType = pole }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Pole_Outside_The_Enum_Should_Fail()
    {
        _validator.Validate(Valid() with { SectionType = (SectionType)42 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_Name_Should_Fail()
    {
        _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_Level_Should_Fail()
    {
        _validator.Validate(Valid() with { Level = "" }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Non_Positive_Coefficient_Should_Fail(int coefficient)
    {
        _validator.Validate(Valid() with { Coefficient = coefficient }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Absurd_Coefficient_Should_Fail()
    {
        _validator.Validate(Valid() with { Coefficient = 100 }).IsValid.Should().BeFalse();
    }
}
