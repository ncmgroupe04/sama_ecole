using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ChangeSchoolStatus;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>Ticket JGK-B01 — le motif est obligatoire ET explicite, même exigence que JGK-A05.</summary>
public class ChangeSchoolStatusCommandValidatorTests
{
    private readonly ChangeSchoolStatusCommandValidator _validator = new();

    private static ChangeSchoolStatusCommand Command(string reason) =>
        new(Guid.NewGuid(), EntityStatus.Suspended, reason);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void An_Empty_Or_Vague_Reason_Should_Be_Rejected(string reason)
    {
        var result = _validator.Validate(Command(reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangeSchoolStatusCommand.Reason));
    }

    [Fact]
    public void An_Explicit_Reason_Should_Be_Accepted()
    {
        var result = _validator.Validate(Command("Impayé constaté après relance."));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Status_Outside_The_Enum_Should_Be_Rejected()
    {
        var command = new ChangeSchoolStatusCommand(Guid.NewGuid(), (EntityStatus)99, "Motif valable");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Reason_Containing_Html_Should_Be_Rejected()
    {
        var result = _validator.Validate(Command("<script>alert(1)</script> impayé"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_SchoolId_Should_Be_Rejected()
    {
        var command = new ChangeSchoolStatusCommand(Guid.Empty, EntityStatus.Suspended, "Motif valable ici");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
