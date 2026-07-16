using FluentAssertions;
using SamaEcole.Application.Grades.Commands.CreateMention;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

public class CreateMentionCommandValidatorTests
{
    private readonly CreateMentionCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_Mention_Passes()
    {
        _validator.Validate(new CreateMentionCommand("Excellent", 16)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Label_Is_Rejected()
    {
        var result = _validator.Validate(new CreateMentionCommand("", 16));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMentionCommand.Label));
    }

    [Fact]
    public void A_Negative_Threshold_Is_Rejected()
    {
        var result = _validator.Validate(new CreateMentionCommand("Excellent", -1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateMentionCommand.MinAverage));
    }
}
