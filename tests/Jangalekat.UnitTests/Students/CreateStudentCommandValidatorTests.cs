using FluentAssertions;
using Jangalekat.Application.Students.Commands.CreateStudent;
using Xunit;

namespace Jangalekat.UnitTests.Students;

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
            Gender = "F",
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }
}
