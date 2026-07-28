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
}
