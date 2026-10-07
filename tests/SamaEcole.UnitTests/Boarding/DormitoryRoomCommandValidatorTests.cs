using FluentAssertions;
using SamaEcole.Application.Boarding.Rooms.CreateDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.UpdateDormitoryRoom;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class DormitoryRoomCommandValidatorTests
{
    private readonly CreateDormitoryRoomCommandValidator _validator = new();
    private readonly UpdateDormitoryRoomCommandValidator _updateValidator = new();

    private static CreateDormitoryRoomCommand Valid() => new()
    {
        DormitoryId = Guid.NewGuid(), Name = "Chambre 102", BedCount = 6
    };

    [Fact] public void Valid_Room_Passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact] public void Empty_Name_Fails() => _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();

    [Fact] public void Name_Longer_Than_100_Fails() =>
        _validator.Validate(Valid() with { Name = new string('x', 101) }).IsValid.Should().BeFalse();

    [Fact] public void Html_In_Name_Fails() =>
        _validator.Validate(Valid() with { Name = "<script>" }).IsValid.Should().BeFalse();

    [Fact] public void Missing_Dormitory_Fails() =>
        _validator.Validate(Valid() with { DormitoryId = Guid.Empty }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(1, true)]
    [InlineData(40, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(41, false)]
    public void Bed_Count_Is_Between_1_And_40(int count, bool expected) =>
        _validator.Validate(Valid() with { BedCount = count }).IsValid.Should().Be(expected);

    [Fact] public void Update_Requires_An_Id_And_A_Name()
    {
        _updateValidator.Validate(new UpdateDormitoryRoomCommand(Guid.NewGuid(), "Chambre 1", 0)).IsValid.Should().BeTrue();
        _updateValidator.Validate(new UpdateDormitoryRoomCommand(Guid.Empty, "Chambre 1", 0)).IsValid.Should().BeFalse();
        _updateValidator.Validate(new UpdateDormitoryRoomCommand(Guid.NewGuid(), "", 0)).IsValid.Should().BeFalse();
    }
}
