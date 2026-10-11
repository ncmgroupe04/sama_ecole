using FluentAssertions;
using SamaEcole.Application.Rooms.Commands.CreateRoom;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Buildings;

public class CreateRoomCommandValidatorTests
{
    private readonly CreateRoomCommandValidator _validator = new();

    private static CreateRoomCommand Valid() => new()
    {
        Name = "Salle 101",
        Capacity = 30,
        Type = RoomType.SalleDeClasse,
        BuildingId = Guid.NewGuid()
    };

    [Fact]
    public void Valid_Room_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Name_Should_Fail()
    {
        _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_Positive_Capacity_Should_Fail(int capacity)
    {
        _validator.Validate(Valid() with { Capacity = capacity }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Absurd_Capacity_Should_Fail()
    {
        _validator.Validate(Valid() with { Capacity = 5000 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_BuildingId_Should_Fail()
    {
        _validator.Validate(Valid() with { BuildingId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Dortoir_Type_Is_Refused_Because_Dormitories_Are_Managed_In_Internat()
    {
        _validator.Validate(Valid() with { Type = RoomType.Dortoir }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_Also_Refuses_The_Dortoir_Type()
    {
        var update = new SamaEcole.Application.Rooms.Commands.UpdateRoom.UpdateRoomCommandValidator();

        update.Validate(new SamaEcole.Application.Rooms.Commands.UpdateRoom.UpdateRoomCommand(
            Guid.NewGuid(), "Salle 101", 30, RoomType.Dortoir, 0)).IsValid.Should().BeFalse();
        update.Validate(new SamaEcole.Application.Rooms.Commands.UpdateRoom.UpdateRoomCommand(
            Guid.NewGuid(), "Salle 101", 30, RoomType.SalleDeClasse, 0)).IsValid.Should().BeTrue();
    }
}
