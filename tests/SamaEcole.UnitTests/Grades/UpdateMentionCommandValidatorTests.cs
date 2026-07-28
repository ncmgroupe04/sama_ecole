using FluentAssertions;
using SamaEcole.Application.Grades.Commands.UpdateMention;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

public class UpdateMentionCommandValidatorTests
{
    private readonly UpdateMentionCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_Update_Passes()
    {
        _validator.Validate(new UpdateMentionCommand(Guid.NewGuid(), "Excellent", 16)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Label_Is_Rejected()
    {
        var result = _validator.Validate(new UpdateMentionCommand(Guid.NewGuid(), "", 16));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateMentionCommand.Label));
    }

    [Fact]
    public void A_Negative_Threshold_Is_Rejected()
    {
        var result = _validator.Validate(new UpdateMentionCommand(Guid.NewGuid(), "Excellent", -1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateMentionCommand.MinAverage));
    }

    [Fact]
    public void An_Empty_Id_Is_Rejected()
    {
        var result = _validator.Validate(new UpdateMentionCommand(Guid.Empty, "Excellent", 16));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateMentionCommand.Id));
    }

    [Fact]
    public void A_Label_Containing_Html_Is_Rejected()
    {
        _validator.Validate(new UpdateMentionCommand(Guid.NewGuid(), "<script>alert(1)</script>", 16))
            .IsValid.Should().BeFalse();
    }
}
