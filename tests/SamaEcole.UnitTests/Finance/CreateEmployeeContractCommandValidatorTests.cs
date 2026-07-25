using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CreateEmployeeContract;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class CreateEmployeeContractCommandValidatorTests
{
    private readonly CreateEmployeeContractCommandValidator _validator = new();

    private static CreateEmployeeContractCommand ValidTeacherContract() => new(
        TeacherId: Guid.NewGuid(), UserId: null, Type: ContractType.Permanent,
        BaseSalary: 250_000m, HourlyRate: 0m, TransportAllowance: 15_000m);

    private static CreateEmployeeContractCommand ValidUserContract() => new(
        TeacherId: null, UserId: Guid.NewGuid(), Type: ContractType.Vacataire,
        BaseSalary: 0m, HourlyRate: 5_000m, TransportAllowance: 0m);

    [Fact]
    public void Valid_Teacher_Contract_Should_Pass()
    {
        _validator.Validate(ValidTeacherContract()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_User_Contract_Should_Pass()
    {
        _validator.Validate(ValidUserContract()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Neither_Teacher_Nor_User_Should_Fail()
    {
        var command = ValidTeacherContract() with { TeacherId = null };

        _validator.Validate(command).IsValid.Should().BeFalse("le contrat doit être lié à quelqu'un");
    }

    [Fact]
    public void Both_Teacher_And_User_Should_Fail()
    {
        var command = ValidTeacherContract() with { UserId = Guid.NewGuid() };

        _validator.Validate(command).IsValid.Should().BeFalse("un contrat ne peut pas être ambigu entre deux personnes");
    }

    [Fact]
    public void Permanent_Contract_Without_Base_Salary_Should_Fail()
    {
        // Sans cette règle, PayrollCalculator produirait silencieusement une fiche de paie à 0.
        var command = ValidTeacherContract() with { BaseSalary = 0m };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Vacataire_Contract_Without_Hourly_Rate_Should_Fail()
    {
        var command = ValidUserContract() with { HourlyRate = 0m };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    public void Negative_Transport_Allowance_Should_Fail(decimal transportAllowance)
    {
        var command = ValidTeacherContract() with { TransportAllowance = transportAllowance };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
