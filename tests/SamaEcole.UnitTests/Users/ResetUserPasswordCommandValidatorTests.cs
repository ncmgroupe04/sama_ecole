using FluentAssertions;
using SamaEcole.Application.Users.Commands.ResetUserPassword;
using Xunit;

namespace SamaEcole.UnitTests.Users;

public class ResetUserPasswordCommandValidatorTests
{
    private readonly ResetUserPasswordCommandValidator _validator = new();

    [Fact]
    public void A_Valid_Command_Should_Pass()
    {
        var command = new ResetUserPasswordCommand(Guid.NewGuid(), "Correct-Horse-9");
        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_UserId_Should_Fail()
    {
        var command = new ResetUserPasswordCommand(Guid.Empty, "Correct-Horse-9");
        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Weak_Password_Should_Fail()
    {
        var command = new ResetUserPasswordCommand(Guid.NewGuid(), "short");
        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
