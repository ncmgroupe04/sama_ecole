using FluentAssertions;
using SamaEcole.Application.Students.Commands.UpdateStudent;
using Xunit;

namespace SamaEcole.UnitTests.Students;

public class UpdateStudentCommandValidatorTests
{
    private readonly UpdateStudentCommandValidator _validator = new();

    private static UpdateStudentCommand ValidCommand(string? fullNameAr = null, string? guardianNameAr = null) =>
        new(
            Guid.NewGuid(), "Awa Fall", new DateOnly(2015, 3, 12), "Dakar", "F", Guid.NewGuid(),
            PhotoUrl: null, GuardianName: null, GuardianPhone: null, GuardianEmail: null, Address: null,
            FullNameAr: fullNameAr, GuardianNameAr: guardianNameAr, RowVersion: 1);

    [Fact]
    public void Should_Succeed_When_FullNameAr_And_GuardianNameAr_Are_Absent()
    {
        var result = _validator.Validate(ValidCommand());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_GuardianNameAr_Exceeds_200_Characters()
    {
        var command = ValidCommand(guardianNameAr: new string('ا', 201));

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.GuardianNameAr));
    }

    [Fact]
    public void Should_Fail_When_FullNameAr_Contains_Html()
    {
        var command = ValidCommand(fullNameAr: "<img src=x onerror=alert(1)>");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FullNameAr));
    }
}
