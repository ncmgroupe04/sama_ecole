using FluentAssertions;
using SamaEcole.Application.Grades.Commands.DeleteMention;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

public class DeleteMentionCommandValidatorTests
{
    private readonly DeleteMentionCommandValidator _validator = new();

    [Fact]
    public void A_Well_Formed_Id_Is_Accepted()
    {
        _validator.Validate(new DeleteMentionCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Id_Is_Rejected()
    {
        var result = _validator.Validate(new DeleteMentionCommand(Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(DeleteMentionCommand.Id));
    }
}
