using FluentAssertions;
using SamaEcole.Application.Boarding.Leaves.DeclareLeave;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class DeclareLeaveValidatorTests
{
    private readonly DeclareLeaveCommandValidator _validator = new();

    private static DeclareLeaveCommand Valid(
        Guid? boarderId = null, DateOnly? leave = null, DateOnly? expected = null, BoardingLeaveReason reason = BoardingLeaveReason.Weekend,
        string? detail = null, string companion = "Fatou Diop") =>
        new(boarderId ?? Guid.NewGuid(), leave ?? new DateOnly(2026, 10, 10), expected ?? new DateOnly(2026, 10, 12), reason, detail, companion);

    [Fact] public void A_Complete_Declaration_Passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact] public void A_Same_Day_Leave_Passes() =>
        _validator.Validate(Valid(leave: new DateOnly(2026, 10, 10), expected: new DateOnly(2026, 10, 10))).IsValid.Should().BeTrue();

    [Fact] public void An_Expected_Return_Before_The_Leave_Is_Refused() =>
        _validator.Validate(Valid(leave: new DateOnly(2026, 10, 10), expected: new DateOnly(2026, 10, 9))).IsValid.Should().BeFalse();

    [Fact] public void A_Missing_Boarder_Is_Refused() => _validator.Validate(Valid(boarderId: Guid.Empty)).IsValid.Should().BeFalse();

    [Fact] public void An_Unknown_Reason_Is_Refused() =>
        _validator.Validate(Valid(reason: (BoardingLeaveReason)99)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Companion_Is_Refused(string companion) => _validator.Validate(Valid(companion: companion)).IsValid.Should().BeFalse();

    [Fact] public void A_Companion_Longer_Than_150_Characters_Is_Refused() =>
        _validator.Validate(Valid(companion: new string('a', 151))).IsValid.Should().BeFalse();

    [Fact] public void A_Detail_Longer_Than_500_Characters_Is_Refused() =>
        _validator.Validate(Valid(detail: new string('a', 501))).IsValid.Should().BeFalse();
}
