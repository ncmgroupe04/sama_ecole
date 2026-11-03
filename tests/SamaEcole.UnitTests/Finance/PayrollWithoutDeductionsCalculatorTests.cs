using FluentAssertions;
using SamaEcole.Application.Finance.Services;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) : la personne reçoit exactement son salaire brut plus
/// sa prime de transport, sans IPRES, CSS, VRS/CFCE ni BRS. Le calcul historique (avec retenues) reste le comportement par
/// défaut et ne doit pas bouger : les deux régimes coexistent dans une même école. Finance : test obligatoire (AGENTS.md).
/// </summary>
public class PayrollWithoutDeductionsCalculatorTests
{
    private static Domain.Entities.FichePaie Calculate(
        decimal baseSalary, decimal hourlyRate = 0m, decimal hours = 0m, decimal transport = 0m, bool? applyDeductions = null)
        => applyDeductions is { } apply
            ? PayrollCalculator.CalculateFichePaie(
                Guid.NewGuid(), Guid.NewGuid(), 3, 2026, baseSalary, hourlyRate, hours, transport, apply)
            : PayrollCalculator.CalculateFichePaie(
                Guid.NewGuid(), Guid.NewGuid(), 3, 2026, baseSalary, hourlyRate, hours, transport);

    [Fact]
    public void Without_Deductions_The_Net_Is_Exactly_The_Gross_Salary()
    {
        // Le cas de l'Oustaz payé 40 000 FCFA : il reçoit 40 000 FCFA, rien n'est retenu.
        var fiche = Calculate(baseSalary: 40_000m, applyDeductions: false);

        fiche.GrossSalary.Should().Be(40_000m);
        fiche.NetSalary.Should().Be(40_000m);
        fiche.DeductionsApplied.Should().BeFalse();
    }

    [Fact]
    public void Without_Deductions_Every_Contribution_And_Withholding_Is_Zero()
    {
        var fiche = Calculate(baseSalary: 300_000m, applyDeductions: false);

        fiche.IpresEmployee.Should().Be(0m);
        fiche.IpresEmployer.Should().Be(0m, "aucune charge patronale non plus : la structure ne cotise pas");
        fiche.CssEmployer.Should().Be(0m);
        fiche.Vrs.Should().Be(0m);
        fiche.Brs.Should().Be(0m);
    }

    [Fact]
    public void Without_Deductions_The_Transport_Allowance_Is_Still_Added_To_The_Net()
    {
        var fiche = Calculate(baseSalary: 40_000m, transport: 5_000m, applyDeductions: false);

        fiche.NetSalary.Should().Be(45_000m);
    }

    [Fact]
    public void Without_Deductions_An_Hourly_Contract_Is_Paid_Hours_Times_Rate_Untouched()
    {
        var fiche = Calculate(baseSalary: 0m, hourlyRate: 2_500m, hours: 16m, applyDeductions: false);

        fiche.GrossSalary.Should().Be(40_000m);
        fiche.NetSalary.Should().Be(40_000m);
    }

    [Fact]
    public void Without_Deductions_A_Salary_Above_The_Ipres_Ceiling_Is_Still_Paid_In_Full()
    {
        // Au-dessus du plafond IPRES (360 000), le régime avec retenues plafonne la base ; sans retenues, rien à plafonner.
        var fiche = Calculate(baseSalary: 500_000m, applyDeductions: false);

        fiche.NetSalary.Should().Be(500_000m);
    }

    [Fact]
    public void Without_Deductions_The_Fiche_Keeps_Its_Identity_Fields()
    {
        var school = Guid.NewGuid();
        var contract = Guid.NewGuid();

        var fiche = PayrollCalculator.CalculateFichePaie(school, contract, 11, 2026, 40_000m, 0m, 0m, 0m, applyDeductions: false);

        fiche.SchoolId.Should().Be(school);
        fiche.EmployeeContractId.Should().Be(contract);
        fiche.Month.Should().Be(11);
        fiche.Year.Should().Be(2026);
        fiche.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void By_Default_The_Standard_Deductions_Are_Still_Applied()
    {
        // Non-régression : sans argument, le calcul historique est inchangé (300 000 : IPRES 5,6 % = 16 800, BRS 5 % de 283 200 = 14 160).
        var fiche = Calculate(baseSalary: 300_000m);

        fiche.DeductionsApplied.Should().BeTrue();
        fiche.IpresEmployee.Should().Be(16_800m);
        fiche.Brs.Should().Be(14_160m);
        fiche.NetSalary.Should().Be(300_000m - 16_800m - 14_160m);
    }

    [Fact]
    public void Asking_Explicitly_For_Deductions_Gives_The_Same_Result_As_The_Default()
    {
        var byDefault = Calculate(baseSalary: 250_000m, transport: 10_000m);
        var explicitTrue = Calculate(baseSalary: 250_000m, transport: 10_000m, applyDeductions: true);

        explicitTrue.NetSalary.Should().Be(byDefault.NetSalary);
        explicitTrue.IpresEmployee.Should().Be(byDefault.IpresEmployee);
        explicitTrue.Brs.Should().Be(byDefault.Brs);
        explicitTrue.Vrs.Should().Be(byDefault.Vrs);
    }
}
