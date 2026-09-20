using SamaEcole.Application.Quran.Commands.CreateQuranProgress;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class CreateQuranProgressCommandValidatorTests
{
    private readonly CreateQuranProgressCommandValidator _validator = new();

    private static CreateQuranProgressCommand Valid() => new(
        Guid.NewGuid(), JuzNumber: 1, HizbNumber: 1, SurahNumber: 1,
        Status: QuranMemorizationStatus.InProcess, EvaluationDate: null, Notes: null);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_StudentId_Should_Fail()
    {
        _validator.Validate(Valid() with { StudentId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void JuzNumber_Out_Of_Range_Should_Fail(int juz)
    {
        _validator.Validate(Valid() with { JuzNumber = juz }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void HizbNumber_Out_Of_Range_Should_Fail(int hizb)
    {
        _validator.Validate(Valid() with { HizbNumber = hizb }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(115)]
    public void SurahNumber_Out_Of_Range_Should_Fail(int surah)
    {
        _validator.Validate(Valid() with { SurahNumber = surah }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Notes_With_Html_Should_Fail()
    {
        _validator.Validate(Valid() with { Notes = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Notes_Too_Long_Should_Fail()
    {
        _validator.Validate(Valid() with { Notes = new string('a', 2001) }).IsValid.Should().BeFalse();
    }
}
