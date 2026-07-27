using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CreateFeeInstallmentPlanCommandValidatorTests
{
    private readonly CreateFeeInstallmentPlanCommandValidator _validator = new();

    private static CreateFeeInstallmentPlanCommand Valid() => new(
        Guid.NewGuid(),
        "Accord du 12/07",
        [
            new CreateFeeInstallmentLine("Versement 1", 40_000m, new DateOnly(2026, 10, 1)),
            new CreateFeeInstallmentLine("Versement 2", 60_000m, new DateOnly(2026, 11, 1))
        ]);

    [Fact]
    public void A_Well_Formed_Plan_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Enrollment_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { EnrollmentId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeInstallmentPlanCommand.EnrollmentId));
    }

    [Fact]
    public void An_Empty_Installment_List_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Installments = [] });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeInstallmentPlanCommand.Installments));
    }

    [Fact]
    public void More_Than_Twenty_Four_Installments_Is_Rejected()
    {
        var installments = Enumerable.Range(1, 25)
            .Select(i => new CreateFeeInstallmentLine($"Versement {i}", 1_000m, new DateOnly(2026, 10, 1).AddMonths(i)))
            .ToList();

        var result = _validator.Validate(Valid() with { Installments = installments });

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Non_Positive_Installment_Amount_Is_Rejected(int amount)
    {
        var result = _validator.Validate(Valid() with
        {
            Installments = [new CreateFeeInstallmentLine("Versement 1", amount, new DateOnly(2026, 10, 1))]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Installment_Label_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with
        {
            Installments = [new CreateFeeInstallmentLine("", 40_000m, new DateOnly(2026, 10, 1))]
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Html_Reason_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { Reason = "<script>alert(1)</script>" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeInstallmentPlanCommand.Reason));
    }

    [Fact]
    public void A_Null_Reason_Is_Accepted()
    {
        _validator.Validate(Valid() with { Reason = null }).IsValid.Should().BeTrue();
    }
}
