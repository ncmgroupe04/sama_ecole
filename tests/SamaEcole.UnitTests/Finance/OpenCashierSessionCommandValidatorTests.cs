using FluentAssertions;
using SamaEcole.Application.Finance.Commands.OpenCashierSession;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class OpenCashierSessionCommandValidatorTests
{
    private readonly OpenCashierSessionCommandValidator _validator = new();

    [Fact]
    public void A_Zero_Or_Positive_Opening_Balance_Is_Accepted()
    {
        _validator.Validate(new OpenCashierSessionCommand(0m)).IsValid.Should().BeTrue();
        _validator.Validate(new OpenCashierSessionCommand(50_000m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Negative_Opening_Balance_Is_Rejected()
    {
        var result = _validator.Validate(new OpenCashierSessionCommand(-1m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenCashierSessionCommand.OpeningBalance));
    }
}
