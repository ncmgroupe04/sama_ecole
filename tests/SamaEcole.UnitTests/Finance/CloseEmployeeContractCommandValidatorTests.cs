using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CloseEmployeeContract;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>Volume 1 §14.1 — clôture d'un contrat : date de fin et motif obligatoires.</summary>
public class CloseEmployeeContractCommandValidatorTests
{
    private readonly CloseEmployeeContractCommandValidator _validator = new();

    private static CloseEmployeeContractCommand Command(
        DateOnly? endDate = null, string reason = "Fin de contrat — départ de l'établissement.") =>
        new(Guid.NewGuid(), endDate ?? new DateOnly(2026, 3, 31), reason, RowVersion: 1);

    [Fact]
    public void A_Valid_Command_Should_Be_Accepted()
    {
        _validator.Validate(Command()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_EndDate_Should_Be_Rejected()
    {
        var command = new CloseEmployeeContractCommand(
            Guid.NewGuid(), default, "Fin de contrat — départ de l'établissement.", 1);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public void An_Empty_Or_Vague_Reason_Should_Be_Rejected(string reason)
    {
        var result = _validator.Validate(Command(reason: reason));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CloseEmployeeContractCommand.Reason));
    }

    [Fact]
    public void A_Reason_Containing_Html_Should_Be_Rejected()
    {
        _validator.Validate(Command(reason: "<script>alert(1)</script> départ")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Empty_ContractId_Should_Be_Rejected()
    {
        var command = new CloseEmployeeContractCommand(
            Guid.Empty, new DateOnly(2026, 3, 31), "Fin de contrat — départ de l'établissement.", 1);

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
