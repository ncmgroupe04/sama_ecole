using FluentAssertions;
using SamaEcole.Application.Teachers.Commands.AssignTeacher;
using Xunit;

namespace SamaEcole.UnitTests.Teachers;

public class AssignTeacherCommandValidatorTests
{
    private readonly AssignTeacherCommandValidator _validator = new();

    private static AssignTeacherCommand Valid() => new()
    {
        TeacherId = Guid.NewGuid(),
        ClassroomId = Guid.NewGuid(),
        SubjectId = Guid.NewGuid()
    };

    [Fact]
    public void Valid_Assignment_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_TeacherId_Should_Fail()
    {
        _validator.Validate(Valid() with { TeacherId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_ClassroomId_Should_Fail()
    {
        _validator.Validate(Valid() with { ClassroomId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_SubjectId_Should_Fail()
    {
        _validator.Validate(Valid() with { SubjectId = Guid.Empty }).IsValid.Should().BeFalse();
    }
}
