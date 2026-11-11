using FluentAssertions;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;
using Outcome = SamaEcole.Application.Finance.Queries.GetMonthlyStatus.MonthlyStatusCalculator.Outcome;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Situation des mensualités : le calcul réutilise l'allocation en cascade d'InstallmentScheduleCalculator.
/// Année scolaire commençant le 1er octobre 2026 ; scolarité 15 000 FCFA × 9 mois ; inscription 10 000 (ponctuelle).
/// </summary>
public class MonthlyStatusCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateTimeOffset EnrolledEarly = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Today = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<EnrollmentFeeLine> Lines() =>
    [
        new() { SchoolId = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(), FeeCategoryId = Guid.NewGuid(),
            Designation = "Inscription", IsRecurring = false, UnitAmount = 10_000m, Months = 1, LineTotal = 10_000m },
        new() { SchoolId = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(), FeeCategoryId = Guid.NewGuid(),
            Designation = "Mensualité", IsRecurring = true, UnitAmount = 15_000m, Months = 9, LineTotal = 135_000m }
    ];

    private static MonthlyStatusCalculator.Result Run(
        decimal totalPaid, int month, IReadOnlyList<(decimal, DateTimeOffset)>? payments = null,
        EnrollmentStatus status = EnrollmentStatus.Confirmed, DateTimeOffset? enrolledAt = null,
        IReadOnlyList<FeeInstallment>? plan = null, bool firstCoversLast = false)
    {
        var schedule = InstallmentScheduleCalculator.Calculate(
            totalPaid, DateOnly.FromDateTime(Today.DateTime), Lines(), Start, plan);
        var year = month >= 10 ? 2026 : 2027;
        return MonthlyStatusCalculator.Compute(
            schedule, month, status, enrolledAt ?? EnrolledEarly, year, payments ?? [], firstCoversLast);
    }

    // ---- Mois choisi par le caissier (option « demander le mois réglé ») ----

    [Fact]
    public void A_Month_Chosen_At_The_Cashier_Wins_Over_The_Order_Rule()
    {
        // Parent : 10 000 d'inscription + 15 000 désignant JUIN. Octobre n'est pas payé, juin l'est.
        var day = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        IReadOnlyList<(int, decimal, DateTimeOffset)> tagged = [(6, 15_000m, day)];
        IReadOnlyList<(decimal, DateTimeOffset)> untagged = [(10_000m, day)];

        var oct = Compute(25_000m, 10, untagged, tagged);
        var jun = Compute(25_000m, 6, untagged, tagged);

        oct.Outcome.Should().Be(Outcome.Unpaid);
        jun.Outcome.Should().Be(Outcome.Paid);
        jun.LastPaymentAt.Should().Be(day);
    }

    [Fact]
    public void The_Months_Of_One_Payment_Are_Settled_Independently_And_Ordinary_Payments_Resume_In_Order()
    {
        var d1 = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        var d2 = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        // Inscription + juin choisi (25 000, le 3/10) ; puis 15 000 non désignés (le 5/11) → octobre.
        IReadOnlyList<(int, decimal, DateTimeOffset)> tagged = [(6, 15_000m, d1)];
        IReadOnlyList<(decimal, DateTimeOffset)> untagged = [(10_000m, d1), (15_000m, d2)];

        Compute(40_000m, 10, untagged, tagged).Outcome.Should().Be(Outcome.Paid);
        Compute(40_000m, 10, untagged, tagged).LastPaymentAt.Should().Be(d2);
        Compute(40_000m, 11, untagged, tagged).Outcome.Should().Be(Outcome.Unpaid);
        Compute(40_000m, 6, untagged, tagged).Outcome.Should().Be(Outcome.Paid);
    }

    private static MonthlyStatusCalculator.Result Compute(
        decimal totalPaid, int month,
        IReadOnlyList<(decimal Amount, DateTimeOffset PaidAt)> untagged,
        IReadOnlyList<(int Month, decimal Amount, DateTimeOffset PaidAt)> tagged)
    {
        var schedule = InstallmentScheduleCalculator.Calculate(
            totalPaid, DateOnly.FromDateTime(Today.DateTime), Lines(), Start, null);
        var year = month >= 10 ? 2026 : 2027;
        return MonthlyStatusCalculator.Compute(
            schedule, month, EnrollmentStatus.Confirmed, EnrolledEarly, year, untagged, false, tagged);
    }

    // ---- Pratique « le mois versé avec l'inscription est le dernier mois (juin) » ----

    [Fact]
    public void With_The_Practice_On_The_Registration_Month_Is_June_And_October_Is_Still_Due()
    {
        // Inscription 10 000 + 15 000 versés ensemble : l'école les range en juin.
        Run(25_000m, month: 6, firstCoversLast: true).Outcome.Should().Be(Outcome.Paid);
        Run(25_000m, month: 10, firstCoversLast: true).Outcome.Should().Be(Outcome.Unpaid);
        Run(25_000m, month: 11, firstCoversLast: true).Outcome.Should().Be(Outcome.Unpaid);
    }

    [Fact]
    public void With_The_Practice_Following_Months_Are_Paid_In_Order_From_October()
    {
        // + 15 000 : octobre soldé, novembre toujours dû (plus de décalage d'un mois).
        Run(40_000m, month: 10, firstCoversLast: true).Outcome.Should().Be(Outcome.Paid);
        Run(40_000m, month: 11, firstCoversLast: true).Outcome.Should().Be(Outcome.Unpaid);
        // + 15 000 : novembre soldé.
        Run(55_000m, month: 11, firstCoversLast: true).Outcome.Should().Be(Outcome.Paid);
        Run(55_000m, month: 12, firstCoversLast: true).Outcome.Should().Be(Outcome.Unpaid);
    }

    [Fact]
    public void With_The_Practice_Paying_Everything_Settles_Every_Month()
    {
        for (var month = 1; month <= 12; month++)
        {
            var r = Run(145_000m, month, firstCoversLast: true);
            r.Outcome.Should().BeOneOf(Outcome.Paid, Outcome.NotConcerned);
        }
    }

    [Fact]
    public void Without_The_Practice_The_Behaviour_Is_Unchanged()
    {
        Run(25_000m, month: 10).Outcome.Should().Be(Outcome.Paid);
        Run(25_000m, month: 6).Outcome.Should().Be(Outcome.Unpaid);
    }

    [Fact]
    public void The_Practice_Leaves_A_Custom_Plan_Untouched()
    {
        var planId = Guid.NewGuid();
        FeeInstallment[] plan =
        [
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "V1", Amount = 40_000m, DueDate = new DateOnly(2026, 10, 5) },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "V2", Amount = 50_000m, DueDate = new DateOnly(2026, 12, 5) }
        ];

        Run(40_000m, month: 10, plan: plan, firstCoversLast: true).Outcome.Should().Be(Outcome.Paid);
        Run(40_000m, month: 12, plan: plan, firstCoversLast: true).Outcome.Should().Be(Outcome.Unpaid);
    }

    [Fact]
    public void Nothing_Paid_Is_Unpaid_With_The_Full_Amount_Remaining()
    {
        var r = Run(0m, month: 10);
        r.Outcome.Should().Be(Outcome.Unpaid);
        r.Expected.Should().Be(15_000m);
        r.Paid.Should().Be(0m);
    }

    [Fact]
    public void The_Exact_Amount_Of_The_Month_Plus_Registration_Is_Paid()
    {
        var day = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var r = Run(25_000m, month: 10, payments: [(25_000m, day)]);
        r.Outcome.Should().Be(Outcome.Paid);
        r.Paid.Should().Be(15_000m);
        r.LastPaymentAt.Should().Be(day);
    }

    [Fact]
    public void A_Partial_Payment_Shows_What_Remains()
    {
        // 10 000 d'inscription absorbent le premier versement de 20 000 : il reste 10 000 pour octobre (15 000) → partiel.
        var r = Run(20_000m, month: 10);
        r.Outcome.Should().Be(Outcome.Partial);
        r.Paid.Should().Be(10_000m);
        r.Expected.Should().Be(15_000m);
    }

    [Fact]
    public void Several_Payments_Are_Summed_And_The_Settling_Payment_Gives_The_Date()
    {
        var first = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        var r = Run(25_000m, month: 10, payments: [(15_000m, first), (10_000m, second)]);
        r.Outcome.Should().Be(Outcome.Paid);
        r.LastPaymentAt.Should().Be(second, "c'est le second versement qui solde la mensualité");
    }

    [Fact]
    public void Later_Months_Are_Unpaid_When_Only_The_First_Is_Covered()
    {
        Run(25_000m, month: 11).Outcome.Should().Be(Outcome.Unpaid);
    }

    [Fact]
    public void A_Student_Enrolled_After_The_Month_Is_Not_Counted_As_Unpaid()
    {
        var r = Run(0m, month: 10, enrolledAt: new DateTimeOffset(2026, 12, 5, 0, 0, 0, TimeSpan.Zero));
        r.Outcome.Should().Be(Outcome.NotConcerned);
    }

    [Fact]
    public void A_Student_Who_Left_Is_Not_Counted_As_Unpaid_But_A_Payment_Is_Never_Hidden()
    {
        Run(0m, month: 11, status: EnrollmentStatus.DroppedOut).Outcome.Should().Be(Outcome.NotConcerned);
        Run(25_000m, month: 10, status: EnrollmentStatus.Transferred).Outcome.Should().Be(Outcome.Paid);
    }

    [Fact]
    public void A_Month_Outside_The_Schedule_Is_Not_Concerned()
    {
        // Juillet : au-delà des 9 mensualités (octobre → juin).
        Run(0m, month: 7).Outcome.Should().Be(Outcome.NotConcerned);
    }

    [Fact]
    public void A_Custom_Plan_Is_Read_By_The_Month_Of_Its_Due_Date()
    {
        var planId = Guid.NewGuid();
        FeeInstallment[] plan =
        [
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 1, Label = "V1", Amount = 40_000m, DueDate = new DateOnly(2026, 10, 5) },
            new() { SchoolId = Guid.NewGuid(), FeeInstallmentPlanId = planId, SequenceNo = 2, Label = "V2", Amount = 50_000m, DueDate = new DateOnly(2026, 12, 5) }
        ];

        Run(40_000m, month: 10, plan: plan).Outcome.Should().Be(Outcome.Paid);
        Run(40_000m, month: 12, plan: plan).Outcome.Should().Be(Outcome.Unpaid);
        Run(60_000m, month: 12, plan: plan).Outcome.Should().Be(Outcome.Partial);
        Run(40_000m, month: 11, plan: plan).Outcome.Should().Be(Outcome.NotConcerned);
    }
}
