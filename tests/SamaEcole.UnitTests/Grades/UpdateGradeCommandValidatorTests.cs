using FluentAssertions;
using SamaEcole.Application.Grades.Commands.UpdateGrade;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

public class UpdateGradeCommandValidatorTests
{
    private readonly UpdateGradeCommandValidator _validator = new();

    private static UpdateGradeCommand Valid() => new(Guid.NewGuid(), 14m, RowVersion: 1);

    [Fact]
    public void A_Well_Formed_Correction_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Id_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Id = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateGradeCommand.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void A_Negative_Value_Is_Rejected(double value)
    {
        var result = _validator.Validate(Valid() with { Value = (decimal)value });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateGradeCommand.Value));
    }
}
