using FluentAssertions;
using SamaEcole.Application.Internat.Commands.CreateInstructor;
using SamaEcole.Application.Internat.Commands.UpdateInstructor;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Internat;

public class CreateInstructorCommandValidatorTests
{
    private readonly CreateInstructorCommandValidator _validator = new();

    private static CreateInstructorCommand Valid() => new("Serigne Modou", "سيرين مودو", "77 123 45 67", Guid.NewGuid());

    [Fact]
    public void Valid_Command_Should_Pass() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact]
    public void Only_The_Name_Is_Mandatory() =>
        _validator.Validate(new CreateInstructorCommand("Serigne Modou", null, null, null)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Name_Should_Fail(string name) =>
        _validator.Validate(Valid() with { FullName = name }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Name_Over_200_Characters_Should_Fail() =>
        _validator.Validate(Valid() with { FullName = new string('x', 201) }).IsValid.Should().BeFalse();

    [Fact]
    public void Html_In_The_Name_Should_Fail() =>
        _validator.Validate(Valid() with { FullName = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Arabic_Name_Over_200_Characters_Should_Fail() =>
        _validator.Validate(Valid() with { FullNameAr = new string('م', 201) }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Invalid_Phone_Should_Fail() =>
        _validator.Validate(Valid() with { Phone = "abc" }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Empty_Guid_Account_Should_Fail() =>
        _validator.Validate(Valid() with { UserId = Guid.Empty }).IsValid.Should().BeFalse();
}

public class UpdateInstructorCommandValidatorTests
{
    private readonly UpdateInstructorCommandValidator _validator = new();

    private static UpdateInstructorCommand Valid() =>
        new(Guid.NewGuid(), "Serigne Modou", null, null, null, EntityStatus.Active, RowVersion: 1);

    [Fact]
    public void Valid_Command_Should_Pass() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(EntityStatus.Active)]
    [InlineData(EntityStatus.Suspended)]
    [InlineData(EntityStatus.Blocked)]
    public void Every_Status_Is_Accepted(EntityStatus status) =>
        _validator.Validate(Valid() with { Status = status }).IsValid.Should().BeTrue();

    [Fact]
    public void An_Undefined_Status_Should_Fail() =>
        _validator.Validate(Valid() with { Status = (EntityStatus)99 }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Empty_Id_Should_Fail() =>
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Blank_Name_Should_Fail() =>
        _validator.Validate(Valid() with { FullName = " " }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Null_Account_Means_Unlink_And_Is_Valid() =>
        _validator.Validate(Valid() with { UserId = null }).IsValid.Should().BeTrue();

    [Fact]
    public void An_Empty_Guid_Account_Should_Fail() =>
        _validator.Validate(Valid() with { UserId = Guid.Empty }).IsValid.Should().BeFalse();
}
