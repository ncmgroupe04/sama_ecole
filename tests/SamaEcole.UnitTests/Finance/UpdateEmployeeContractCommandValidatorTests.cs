using FluentAssertions;
using SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Volume 1 §14.1 — modification d'un contrat actif : montants non négatifs, motif obligatoire et explicite.</summary>
public class UpdateEmployeeContractCommandValidatorTests
{
    private readonly UpdateEmployeeContractCommandValidator _validator = new();

    private static UpdateEmployeeContractCommand Command(
        decimal baseSalary = 250_000m, decimal hourlyRate = 0m, decimal transportAllowance = 15_000m,
        string reason = "Augmentation annuelle actée en conseil.") =>
        new(Guid.NewGuid(), baseSalary, hourlyRate, transportAllowance, reason, RowVersion: 1);

    [Fact]
    public void A_Valid_Command_Should_Be_Accepted()
    {
        _validator.Validate(Command()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    public void A_Negative_BaseSalary_Should_Be_Rejected(decimal value)
    {
        _validator.Validate(Command(baseSalary: value)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    public void A_Negative_HourlyRate_Should_Be_Rejected(decimal value)
    {
        _validator.Validate(Command(hourlyRate: value)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void An_Empty_Or_Vague_Reason_Should_Be_Rejected(string reason)
    {
        var result = _validator.Validate(Command(reason: reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateEmployeeContractCommand.Reason));
    }

    [Fact]
    public void A_Reason_Containing_Html_Should_Be_Rejected()
    {
        _validator.Validate(Command(reason: "<script>alert(1)</script> augmentation")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_ContractId_Should_Be_Rejected()
    {
        var command = new UpdateEmployeeContractCommand(
            Guid.Empty, 250_000m, 0m, 15_000m, "Augmentation annuelle actée en conseil.", 1);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
