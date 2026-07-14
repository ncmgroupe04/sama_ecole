using FluentAssertions;
using SamaEcole.Application.Users.Commands.ChangeUserStatus;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Users;

/// <summary>Ticket JGK-A05 — le motif est obligatoire ET explicite.</summary>
public class ChangeUserStatusCommandValidatorTests
{
    private readonly ChangeUserStatusCommandValidator _validator = new();

    private static ChangeUserStatusCommand Command(string reason) =>
        new(Guid.NewGuid(), EntityStatus.Suspended, reason);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")] // trop court pour documenter quoi que ce soit
    public void An_Empty_Or_Vague_Reason_Should_Be_Rejected(string reason)
    {
        var result = _validator.Validate(Command(reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ChangeUserStatusCommand.Reason));
    }

    [Fact]
    public void An_Explicit_Reason_Should_Be_Accepted()
    {
        var result = _validator.Validate(Command("Absences répétées non justifiées"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Status_Outside_The_Enum_Should_Be_Rejected()
    {
        var command = new ChangeUserStatusCommand(Guid.NewGuid(), (EntityStatus)99, "Motif valable");

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
