using FluentAssertions;
using SamaEcole.Application.Boarding.Beds.ChangeBedStatus;
using SamaEcole.Application.Boarding.Beds.CreateBed;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class BedCommandValidatorTests
{
    private readonly CreateBedCommandValidator _create = new();
    private readonly ChangeBedStatusCommandValidator _status = new();

    [Fact] public void A_Bed_Without_An_Explicit_Number_Passes() =>
        _create.Validate(new CreateBedCommand { DormitoryRoomId = Guid.NewGuid() }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(1, true)]
    [InlineData(999, true)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    [InlineData(1000, false)]
    public void An_Explicit_Number_Is_Between_1_And_999(int number, bool expected) =>
        _create.Validate(new CreateBedCommand { DormitoryRoomId = Guid.NewGuid(), BedNumber = number }).IsValid.Should().Be(expected);

    [Fact] public void A_Room_Is_Required() =>
        _create.Validate(new CreateBedCommand { DormitoryRoomId = Guid.Empty }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(BedStatus.Available, true)]
    [InlineData(BedStatus.Maintenance, true)]
    [InlineData(BedStatus.Occupied, false)]   // un lit devient occupé par une affectation, pas par un changement de statut
    [InlineData((BedStatus)99, false)]
    public void Status_Can_Only_Switch_Between_Available_And_Maintenance(BedStatus status, bool expected) =>
        _status.Validate(new ChangeBedStatusCommand(Guid.NewGuid(), status, 0)).IsValid.Should().Be(expected);

    [Fact] public void Status_Change_Requires_An_Id() =>
        _status.Validate(new ChangeBedStatusCommand(Guid.Empty, BedStatus.Maintenance, 0)).IsValid.Should().BeFalse();
}
