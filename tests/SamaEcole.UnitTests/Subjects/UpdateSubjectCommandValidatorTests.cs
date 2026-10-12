using FluentAssertions;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subjects;

public class UpdateSubjectCommandValidatorTests
{
    private readonly UpdateSubjectCommandValidator _validator = new();

    private static UpdateSubjectCommand Valid() =>
        new(Guid.NewGuid(), "Mathématiques", "Primaire", 4, RowVersion: 1);

    [Fact]
    public void An_Update_Without_A_Pole_Should_Pass_And_Leave_It_Untouched()
    {
        var command = Valid();

        command.SectionType.Should().BeNull("null = pôle inchangé, jamais un retour à la section française");
        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(SectionType.French)]
    [InlineData(SectionType.Arabic)]
    [InlineData(SectionType.IslamicStudies)]
    [InlineData(SectionType.Common)]
    public void Every_Pole_Should_Be_Accepted(SectionType pole)
    {
        _validator.Validate(Valid() with { SectionType = pole }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Pole_Outside_The_Enum_Should_Fail()
    {
        _validator.Validate(Valid() with { SectionType = (SectionType)42 }).IsValid.Should().BeFalse();
    }
}
