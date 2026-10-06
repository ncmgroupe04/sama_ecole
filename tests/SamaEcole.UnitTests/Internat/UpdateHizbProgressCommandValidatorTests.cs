using FluentAssertions;
using SamaEcole.Application.Internat.Commands.UpdateHizbProgress;
using Xunit;

namespace SamaEcole.UnitTests.Internat;

public class UpdateHizbProgressCommandValidatorTests
{
    private readonly UpdateHizbProgressCommandValidator _validator = new();

    private static UpdateHizbProgressCommand Valid() => new(Guid.NewGuid(), 12, 2, 4, RowVersion: null);

    [Fact]
    public void Valid_Command_Should_Pass() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(-1)]
    public void Hizb_Number_Out_Of_1_To_60_Should_Fail(int hizb) =>
        _validator.Validate(Valid() with { HizbNumber = hizb }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void Hizb_Number_Bounds_Are_Inclusive(int hizb) =>
        _validator.Validate(Valid() with { HizbNumber = hizb }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Quarters_Out_Of_0_To_4_Should_Fail(int quarters) =>
        _validator.Validate(Valid() with { CompletedQuarters = quarters, Rating = null }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_Out_Of_1_To_5_Should_Fail(int rating) =>
        _validator.Validate(Valid() with { Rating = rating }).IsValid.Should().BeFalse();

    [Fact]
    public void Rating_Is_Optional_When_The_Hizb_Is_Started() =>
        _validator.Validate(Valid() with { Rating = null }).IsValid.Should().BeTrue();

    [Fact]
    public void A_Hizb_Not_Started_Cannot_Have_A_Rating() =>
        _validator.Validate(Valid() with { CompletedQuarters = 0, Rating = 3 }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Hizb_Not_Started_Without_Rating_Is_Valid() =>
        _validator.Validate(Valid() with { CompletedQuarters = 0, Rating = null }).IsValid.Should().BeTrue();

    [Fact]
    public void Empty_Student_Should_Fail() =>
        _validator.Validate(Valid() with { StudentId = Guid.Empty }).IsValid.Should().BeFalse();
}
