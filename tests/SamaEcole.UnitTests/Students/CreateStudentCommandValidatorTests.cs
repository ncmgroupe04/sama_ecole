using FluentAssertions;
using SamaEcole.Application.Students.Commands.CreateStudent;
using Xunit;

namespace SamaEcole.UnitTests.Students;

public class CreateStudentCommandValidatorTests
{
    private readonly CreateStudentCommandValidator _validator = new();

    [Fact]
    public void Should_Fail_When_Gender_Is_Invalid()
    {
        var command = new CreateStudentCommand
        {
            FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "X",
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.Gender));
    }

    [Fact]
    public void Should_Succeed_When_All_Fields_Are_Valid()
    {
        var command = new CreateStudentCommand
        {
            FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_BirthPlace_Is_Empty()
    {
        // Feature E — lieu de naissance obligatoire (exigence juridique/académique au Sénégal).
        var command = new CreateStudentCommand
        {
            FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "",
            Gender = "F",
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.BirthPlace));
    }

    [Fact]
    public void Should_Fail_When_FullName_Contains_Html()
    {
        // JGK-F01 — branchement de la règle NoHtml (payloads exhaustifs : SafeTextValidationTests).
        var command = new CreateStudentCommand
        {
            FullName = "<img src=x onerror=alert(1)>",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FullName));
    }
}
