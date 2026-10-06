using FluentAssertions;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Commands.AssignInstructor;
using Xunit;

namespace SamaEcole.UnitTests.Internat;

public class AssignInstructorCommandValidatorTests
{
    private readonly AssignInstructorCommandValidator _validator = new();

    private static AssignInstructorCommand Valid() => new(Guid.NewGuid(), [Guid.NewGuid()]);

    [Fact]
    public void Valid_Command_Should_Pass() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void A_Null_Instructor_Means_Detach_And_Is_Valid() =>
        _validator.Validate(Valid() with { InstructorId = null }).IsValid.Should().BeTrue();

    [Fact]
    public void An_Empty_Guid_Instructor_Should_Fail() =>
        _validator.Validate(Valid() with { InstructorId = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Empty_Student_List_Should_Fail() =>
        _validator.Validate(Valid() with { StudentIds = [] }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Empty_Guid_Student_Should_Fail() =>
        _validator.Validate(Valid() with { StudentIds = [Guid.Empty] }).IsValid.Should().BeFalse();

    [Fact]
    public void The_Batch_Is_Bounded()
    {
        var atLimit = Enumerable.Range(0, HizbRules.MaxStudentsPerAssignment).Select(_ => Guid.NewGuid()).ToList();
        _validator.Validate(Valid() with { StudentIds = atLimit }).IsValid.Should().BeTrue();

        atLimit.Add(Guid.NewGuid());
        _validator.Validate(Valid() with { StudentIds = atLimit }).IsValid.Should().BeFalse();
    }
}
