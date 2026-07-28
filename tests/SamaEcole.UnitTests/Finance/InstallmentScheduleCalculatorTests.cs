using FluentAssertions;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Étape 5 — même algorithme d'allocation en cascade que GetStudentBalanceQuery/GetDuesNoticeQuery
/// utilisaient avant leur extraction ici : ces tests verrouillent la non-régression du comportement
/// dérivé (aucun échéancier personnalisé) ET couvrent le nouveau chemin (plan personnalisé).
/// </summary>
public class InstallmentScheduleCalculatorTests
{
    private static readonly DateOnly SchoolYearStart = new(2026, 10, 1);

    private static EnrollmentFeeLine RecurringLine(decimal unitAmount, int months) => new()
    {
        SchoolId = Guid.NewGuid(),
        EnrollmentId = Guid.NewGuid(),
        FeeCategoryId = Guid.NewGuid(),
        Designation = "Mensualité",
        IsRecurring = true,
        UnitAmount = unitAmount,
        Months = months,
        LineTotal = unitAmount * months
    };

    private static EnrollmentFeeLine OneOffLine(decimal amount) => new()
    {
        SchoolId = Guid.NewGuid(),
        EnrollmentId = Guid.NewGuid(),
        FeeCategoryId = Guid.NewGuid(),
        Designation = "Inscription",
        IsRecurring = false,
        UnitAmount = amount,
        Months = 1,
        LineTotal = amount
    };

    [Fact]
    public void Derived_Schedule_Marks_Fully_Paid_Installments_As_Paid()
    {
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 3) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 30_000m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(i => i.Status == InstallmentScheduleCalculator.InstallmentState.Paid);
    }

    [Fact]
    public void Derived_Schedule_Allocates_Payment_To_Oldest_Installment_First()
    {
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 3) };

        // Un seul versement de 15 000 : la 1ère mensualité (mois 1) doit être Paid, la 2e Partial,
        // la 3e Pending (échue plus tard) — jamais une répartition égale entre les trois.
        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 15_000m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result[0].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Paid);
        result[0].RemainingDue.Should().Be(0m);
        result[1].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Partial);
        result[1].RemainingDue.Should().Be(5_000m);
        result[2].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Pending);
        result[2].RemainingDue.Should().Be(10_000m);
    }

    [Fact]
    public void Derived_Schedule_Marks_Unpaid_Past_Due_Installment_As_Overdue()
    {
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 2) };
        var farInTheFuture = SchoolYearStart.AddMonths(6);

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: farInTheFuture, lines, SchoolYearStart, customInstallments: null);

        result.Should().OnlyContain(i => i.Status == InstallmentScheduleCalculator.InstallmentState.Overdue);
    }

    [Fact]
    public void One_Off_Line_Produces_A_Single_Installment_Never_Multiplied_By_Months()
    {
        var lines = new List<EnrollmentFeeLine> { OneOffLine(5_000m) };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 0m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments: null);

        result.Should().ContainSingle();
        result[0].Amount.Should().Be(5_000m);
        result[0].DueDate.Should().Be(SchoolYearStart);
    }

    [Fact]
    public void Custom_Plan_Takes_Priority_Over_Derived_Lines_When_Present()
    {
        // Des lignes dérivées existent (comme sur toute inscription), mais un plan personnalisé est
        // actif : lui seul doit produire les échéances retournées.
        var lines = new List<EnrollmentFeeLine> { RecurringLine(10_000m, 9) };
        var planId = Guid.NewGuid();
        var customInstallments = new List<FeeInstallment>
        {
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "Versement 1", Amount = 40_000m, DueDate = SchoolYearStart },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "Versement 2", Amount = 50_000m, DueDate = SchoolYearStart.AddMonths(1) }
        };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 40_000m, today: SchoolYearStart, lines, SchoolYearStart, customInstallments);

        result.Should().HaveCount(2);
        result[0].Label.Should().Be("Versement 1");
        result[0].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Paid);
        result[1].Label.Should().Be("Versement 2");
        result[1].RemainingDue.Should().Be(50_000m);
    }

    [Fact]
    public void Custom_Plan_Installments_Are_Allocated_In_SequenceNo_Order_Regardless_Of_Input_Order()
    {
        var planId = Guid.NewGuid();
        // Volontairement fourni hors ordre : SequenceNo doit primer, pas l'ordre de la liste.
        var customInstallments = new List<FeeInstallment>
        {
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "Second", Amount = 30_000m, DueDate = SchoolYearStart.AddMonths(1) },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "First", Amount = 20_000m, DueDate = SchoolYearStart }
        };

        var result = InstallmentScheduleCalculator.Calculate(
            totalAmountPaid: 20_000m, today: SchoolYearStart, [], SchoolYearStart, customInstallments);

        result[0].Label.Should().Be("First");
        result[0].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Paid);
        result[1].Label.Should().Be("Second");
        result[1].Status.Should().Be(InstallmentScheduleCalculator.InstallmentState.Pending);
    }
}
