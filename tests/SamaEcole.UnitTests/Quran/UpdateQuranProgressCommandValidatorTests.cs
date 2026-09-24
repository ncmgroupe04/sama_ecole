using SamaEcole.Application.Quran.Commands.UpdateQuranProgress;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class UpdateQuranProgressCommandValidatorTests
{
    private readonly UpdateQuranProgressCommandValidator _validator = new();

    private static UpdateQuranProgressCommand Valid() => new(
        Guid.NewGuid(), QuranMemorizationStatus.Memorized, null, null, RowVersion: 1);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Notes_With_Html_Should_Fail()
    {
        _validator.Validate(Valid() with { Notes = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }
}
