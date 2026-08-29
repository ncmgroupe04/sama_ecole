using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CloseCashierSession;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CloseCashierSessionCommandValidatorTests
{
    private readonly CloseCashierSessionCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_Command_Is_Accepted()
    {
        _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid(), 25_000m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_SessionId_Is_Rejected()
    {
        var result = _validator.Validate(new CloseCashierSessionCommand(Guid.Empty, 25_000m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.SessionId));
    }

    [Fact]
    public void A_Negative_ActualCashAmount_Is_Rejected()
    {
        var result = _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid(), -1m));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.ActualCashAmount));
    }

    [Fact]
    public void A_Zero_ActualCashAmount_Is_Accepted()
    {
        // Une caisse vidée à zéro reste un montant compté valide (JGK-F09) — ce n'est jamais rejeté
        // pour cette seule raison, seul un montant négatif n'a pas de sens physique.
        _validator.Validate(new CloseCashierSessionCommand(Guid.NewGuid(), 0m)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_DiscrepancyReason_Over_500_Characters_Is_Rejected()
    {
        var result = _validator.Validate(
            new CloseCashierSessionCommand(Guid.NewGuid(), 25_000m, new string('x', 501)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseCashierSessionCommand.DiscrepancyReason));
    }
}
