using FluentAssertions;
using SamaEcole.Application.Students.Commands.CorrectStudentMatricule;
using Xunit;

namespace SamaEcole.UnitTests.Students;

public class CorrectStudentMatriculeCommandValidatorTests
{
    private readonly CorrectStudentMatriculeCommandValidator _validator = new();

    private static CorrectStudentMatriculeCommand Valid() =>
        new(Guid.NewGuid(), "MONECOLE-2026-0042", RowVersion: 1);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Matricule_Should_Fail(string matricule)
    {
        _validator.Validate(Valid() with { NewMatricule = matricule }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Matricule_Longer_Than_Thirty_Chars_Should_Fail()
    {
        _validator.Validate(Valid() with { NewMatricule = new string('A', 31) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Matricule_Of_Thirty_Chars_Should_Pass()
    {
        _validator.Validate(Valid() with { NewMatricule = new string('A', 30) }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("<b>0042</b>")]
    [InlineData("javascript:x")]
    public void Unsafe_Matricule_Should_Fail(string matricule)
    {
        _validator.Validate(Valid() with { NewMatricule = matricule }).IsValid.Should().BeFalse();
    }
}
