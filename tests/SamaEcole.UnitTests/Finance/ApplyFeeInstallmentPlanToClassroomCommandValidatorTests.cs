using FluentAssertions;
using SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class ApplyFeeInstallmentPlanToClassroomCommandValidatorTests
{
    private readonly ApplyFeeInstallmentPlanToClassroomCommandValidator _validator = new();

    private static ApplyFeeInstallmentPlanToClassroomCommand Valid() => new(
        Guid.NewGuid(),
        "Facilité de fin d'année",
        [
            new InstallmentTemplateLine("Versement 1", 0.4m, 0),
            new InstallmentTemplateLine("Versement 2", 0.3m, 30),
            new InstallmentTemplateLine("Versement 3", 0.3m, 60)
        ]);

    [Fact]
    public void A_Well_Formed_Template_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Classroom_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { ClassroomId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApplyFeeInstallmentPlanToClassroomCommand.ClassroomId));
    }

    [Fact]
    public void Percentages_That_Do_Not_Sum_To_One_Hundred_Percent_Are_Rejected()
    {
        var result = _validator.Validate(Valid() with
        {
            Template = [new InstallmentTemplateLine("Versement unique", 0.9m, 0)]
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ApplyFeeInstallmentPlanToClassroomCommand.Template));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void A_Percentage_Outside_Zero_Exclusive_To_One_Inclusive_Is_Rejected(decimal percentage)
    {
        var result = _validator.Validate(Valid() with
        {
            Template = [new InstallmentTemplateLine("Versement unique", percentage, 0)]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Negative_Offset_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with
        {
            Template = [new InstallmentTemplateLine("Versement unique", 1m, -1)]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Template_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Template = [] });

        result.IsValid.Should().BeFalse();
    }
}
