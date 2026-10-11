using FluentAssertions;
using SamaEcole.Application.Boarding.Boarders.AssignBed;
using SamaEcole.Application.Boarding.Boarders.EndBoarding;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class BoarderCommandValidatorTests
{
    private readonly AssignBedCommandValidator _assign = new();
    private readonly EndBoardingCommandValidator _end = new();

    [Fact]
    public void Interne_With_A_Bed_Is_Valid() =>
        _assign.Validate(new AssignBedCommand(Guid.NewGuid(), BoardingRegime.Interne, Guid.NewGuid(), false, null))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Interne_Without_A_Bed_Is_Invalid() =>
        _assign.Validate(new AssignBedCommand(Guid.NewGuid(), BoardingRegime.Interne, null, false, null))
            .Errors.Should().Contain(e => e.PropertyName == "BedId");

    [Fact]
    public void Half_Board_Without_A_Bed_Is_Valid() =>
        _assign.Validate(new AssignBedCommand(Guid.NewGuid(), BoardingRegime.DemiPensionnaire, null, true, null))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Half_Board_With_A_Bed_Is_Invalid() =>
        _assign.Validate(new AssignBedCommand(Guid.NewGuid(), BoardingRegime.DemiPensionnaire, Guid.NewGuid(), false, null))
            .Errors.Should().Contain(e => e.PropertyName == "BedId");

    [Fact]
    public void An_Unknown_Regime_Value_Is_Invalid() =>
        _assign.Validate(new AssignBedCommand(Guid.NewGuid(), (BoardingRegime)99, Guid.NewGuid(), false, null))
            .Errors.Should().Contain(e => e.PropertyName == "Regime");

    [Fact]
    public void An_Enrollment_Is_Required() =>
        _assign.Validate(new AssignBedCommand(Guid.Empty, BoardingRegime.Interne, Guid.NewGuid(), false, null))
            .Errors.Should().Contain(e => e.PropertyName == "EnrollmentId");

    [Fact]
    public void Ending_Requires_A_Boarder() =>
        _end.Validate(new EndBoardingCommand(Guid.Empty, 0)).IsValid.Should().BeFalse();
}
