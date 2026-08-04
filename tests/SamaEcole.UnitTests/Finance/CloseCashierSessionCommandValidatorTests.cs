using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CloseCashierSessionCommandValidatorTests
{
    private readonly CloseCashierSessionCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_SessionId_Is_Accepted()
    {
        _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_SessionId_Is_Rejected()
    {
        var result = _validator.Validate(new CloseCashierSessionCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.SessionId));
    }
}
