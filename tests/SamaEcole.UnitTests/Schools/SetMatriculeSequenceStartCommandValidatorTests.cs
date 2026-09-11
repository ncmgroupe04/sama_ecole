using FluentAssertions;
using SamaEcole.Application.Schools.Commands.SetMatriculeSequenceStart;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

public class SetMatriculeSequenceStartCommandValidatorTests
{
    private readonly SetMatriculeSequenceStartCommandValidator _validator = new();

    private static SetMatriculeSequenceStartCommand Valid() => new(MatriculeKind.Student, 1000);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(MatriculeKind.Student)]
    [InlineData(MatriculeKind.Teacher)]
    public void Student_And_Teacher_Kinds_Are_Accepted(MatriculeKind kind)
    {
        _validator.Validate(Valid() with { Kind = kind }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(MatriculeKind.Receipt)]
    [InlineData(MatriculeKind.MutationCertificate)]
    [InlineData(MatriculeKind.ProvisionalIen)]
    public void Non_Student_Or_Teacher_Kinds_Are_Rejected(MatriculeKind kind)
    {
        _validator.Validate(Valid() with { Kind = kind }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-500)]
    public void NextValue_Below_One_Should_Fail(int nextValue)
    {
        _validator.Validate(Valid() with { NextValue = nextValue }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NextValue_Of_One_Should_Pass()
    {
        _validator.Validate(Valid() with { NextValue = 1 }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void NextValue_Above_The_Cap_Should_Fail()
    {
        _validator.Validate(Valid() with { NextValue = 1_000_000 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void NextValue_At_The_Cap_Should_Pass()
    {
        _validator.Validate(Valid() with { NextValue = 999_999 }).IsValid.Should().BeTrue();
    }
}
