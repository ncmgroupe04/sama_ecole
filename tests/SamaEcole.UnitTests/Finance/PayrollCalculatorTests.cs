using FluentAssertions;
using SamaEcole.Application.Finance.Services;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Module Comptabilité & Fiscalité (JGK) — le calcul des cotisations sociales sénégalaises (IPRES, CSS,
/// VRS/CFCE, BRS) est le cœur du bulletin de paie : une erreur ici se répercute sur ce que l'école doit
/// à ses caisses sociales ET sur ce qu'elle verse à l'employé. AGENTS.md interdit de livrer du code
/// Finance sans test associé — ces cas couvrent le calcul horaire vs mensuel, le plafonnement IPRES/CSS,
/// et la base imposable du BRS.
/// </summary>
public class PayrollCalculatorTests
{
    [Fact]
    public void Permanent_Contract_Uses_Base_Salary_Not_Hours()
    {
        // Un contrat Permanent (HourlyRate = 0) doit ignorer HoursWorked et retenir BaseSalary.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 300_000m, hourlyRate: 0m, hoursWorked: 999m, transportAllowance: 0m);

        fiche.GrossSalary.Should().Be(300_000m, "un contrat Permanent se base sur le salaire fixe, pas sur des heures");
    }

    [Fact]
    public void Vacataire_Contract_Uses_Hours_Times_Rate_Not_Base_Salary()
    {
        // Un contrat Vacataire (HourlyRate > 0) doit ignorer BaseSalary et calculer Heures × Taux.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 999_999m, hourlyRate: 5_000m, hoursWorked: 40m, transportAllowance: 0m);

        fiche.GrossSalary.Should().Be(200_000m, "40h × 5 000 FCFA, BaseSalary ne doit pas entrer en jeu");
    }

    [Fact]
    public void Ipres_Contribution_Is_Capped_At_The_Monthly_Ceiling()
    {
        // Brut très supérieur au plafond IPRES (360 000) : la base de calcul doit être plafonnée, pas le brut réel.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 1_000_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.IpresEmployee.Should().Be(360_000m * PayrollCalculator.IpresEmployeeRate,
            "la base IPRES est plafonnée à 360 000 FCFA, jamais le brut réel au-delà");
        fiche.IpresEmployer.Should().Be(360_000m * PayrollCalculator.IpresEmployerRate);
    }

    [Fact]
    public void Css_Contribution_Is_Capped_At_Its_Own_Lower_Ceiling()
    {
        // CSS a un plafond distinct (63 000), plus bas que celui d'IPRES — les deux bases ne doivent
        // jamais être confondues.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 200_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.CssEmployer.Should().Be(63_000m * PayrollCalculator.CssEmployerRate,
            "200 000 dépasse le plafond CSS (63 000) : la base doit être plafonnée");
    }

    [Fact]
    public void Below_Ceiling_Salary_Is_Not_Capped()
    {
        // Sous les deux plafonds : la base de calcul est le brut réel, sans troncature.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 50_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.IpresEmployee.Should().Be(50_000m * PayrollCalculator.IpresEmployeeRate);
        fiche.CssEmployer.Should().Be(50_000m * PayrollCalculator.CssEmployerRate);
    }

    [Fact]
    public void Brs_Is_Computed_On_Gross_Minus_Employee_Ipres_Not_On_Gross_Alone()
    {
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 100_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 0m);

        var expectedTaxableBase = 100_000m - fiche.IpresEmployee;
        fiche.Brs.Should().Be(expectedTaxableBase * PayrollCalculator.BrsRate,
            "le BRS se calcule sur le brut APRÈS déduction de l'IPRES salarié, pas sur le brut seul");
    }

    [Fact]
    public void Net_Salary_Adds_Transport_And_Subtracts_Only_Employee_Side_Deductions()
    {
        // Le net à payer n'intègre QUE les retenues salariales (IPRES employé, BRS) — jamais les parts
        // patronales (IPRES employeur, CSS, VRS), qui restent à la charge de l'établissement.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 100_000m, hourlyRate: 0m, hoursWorked: 0m, transportAllowance: 15_000m);

        var expectedNet = fiche.GrossSalary + 15_000m - fiche.IpresEmployee - fiche.Brs;
        fiche.NetSalary.Should().Be(expectedNet);
        fiche.NetSalary.Should().BeLessThan(fiche.GrossSalary + 15_000m, "des retenues salariales existent forcément sur un salaire positif");
    }

    [Fact]
    public void Zero_Gross_Salary_Never_Produces_A_Negative_Brs()
    {
        // Contrat Vacataire sans heures saisies (0h) : brut nul, aucune cotisation ne doit devenir négative.
        var fiche = PayrollCalculator.CalculateFichePaie(
            schoolId: Guid.NewGuid(), contractId: Guid.NewGuid(), month: 3, year: 2026,
            baseSalary: 0m, hourlyRate: 5_000m, hoursWorked: 0m, transportAllowance: 0m);

        fiche.GrossSalary.Should().Be(0m);
        fiche.Brs.Should().Be(0m, "une base imposable nulle ou négative ne doit jamais produire une retenue négative");
        fiche.NetSalary.Should().Be(0m);
    }
}
