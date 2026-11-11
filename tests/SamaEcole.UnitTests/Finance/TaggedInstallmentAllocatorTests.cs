using FluentAssertions;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Allocateur : mois choisis d'abord, puis imputation « dans l'ordre » pour le reste. Sans mois choisis et sans la pratique
/// d'école, il doit être IDENTIQUE à InstallmentScheduleCalculator (aucun chiffre existant ne bouge).
/// Inscription 10 000 + 9 mensualités de 15 000, année commençant le 1er octobre 2026.
/// </summary>
public class TaggedInstallmentAllocatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly Today = new(2026, 12, 1);

    private static List<EnrollmentFeeLine> Lines() =>
    [
        new() { SchoolId = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(), FeeCategoryId = Guid.NewGuid(),
            Designation = "Inscription", IsRecurring = false, UnitAmount = 10_000m, Months = 1, LineTotal = 10_000m },
        new() { SchoolId = Guid.NewGuid(), EnrollmentId = Guid.NewGuid(), FeeCategoryId = Guid.NewGuid(),
            Designation = "Mensualité", IsRecurring = true, UnitAmount = 15_000m, Months = 9, LineTotal = 135_000m }
    ];

    private static IReadOnlyList<InstallmentScheduleCalculator.CalculatedInstallment> Schedule(decimal paid) =>
        InstallmentScheduleCalculator.Calculate(paid, Today, Lines(), Start, null);

    private static decimal PaidFor(IReadOnlyList<TaggedInstallmentAllocator.Allocated> a, int month) =>
        a.Where(x => !x.Installment.Id.StartsWith("LINE-") && x.Installment.DueDate.Month == month)
            .Sum(x => x.Installment.AmountPaid);

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)]
    [InlineData(25_000)]
    [InlineData(47_500)]
    [InlineData(145_000)]
    public void Without_Tags_Or_Practice_The_Result_Equals_The_Historical_Calculator(int paid)
    {
        var schedule = Schedule(paid);

        var result = TaggedInstallmentAllocator.Allocate(schedule, paid, null, Today, firstMonthCoversLast: false);

        result.Select(a => (a.Installment.Id, a.Installment.AmountPaid, a.Installment.RemainingDue, a.Installment.Status))
            .Should().Equal(schedule.Select(s => (s.Id, s.AmountPaid, s.RemainingDue, s.Status)));
    }

    [Fact]
    public void A_Tagged_Month_Receives_Its_Money_And_The_Rest_Fills_In_Order()
    {
        // 25 000 au total : 15 000 désignent juin, 10 000 non désignés → inscription. Octobre reste dû.
        var result = TaggedInstallmentAllocator.Allocate(
            Schedule(25_000m), 25_000m, new Dictionary<int, decimal> { [6] = 15_000m }, Today, false);

        PaidFor(result, 6).Should().Be(15_000m);
        PaidFor(result, 10).Should().Be(0m);
        result.Single(a => a.Installment.Id.StartsWith("LINE-")).Installment.AmountPaid.Should().Be(10_000m);
    }

    [Fact]
    public void Untagged_Money_Then_Skips_The_Already_Settled_Tagged_Month()
    {
        // juin désigné (15 000) + 40 000 non désignés : inscription, puis octobre, novembre ; juin n'est pas payé deux fois.
        var result = TaggedInstallmentAllocator.Allocate(
            Schedule(55_000m), 55_000m, new Dictionary<int, decimal> { [6] = 15_000m }, Today, false);

        PaidFor(result, 6).Should().Be(15_000m);
        PaidFor(result, 10).Should().Be(15_000m);
        PaidFor(result, 11).Should().Be(15_000m);
        PaidFor(result, 12).Should().Be(0m);
    }

    [Fact]
    public void A_Tagged_Amount_Above_The_Month_Overflows_Into_The_Ordinary_Allocation()
    {
        // 20 000 désignent octobre (dû : 15 000) : 5 000 retombent dans le remplissage normal (inscription).
        var result = TaggedInstallmentAllocator.Allocate(
            Schedule(20_000m), 20_000m, new Dictionary<int, decimal> { [10] = 20_000m }, Today, false);

        PaidFor(result, 10).Should().Be(15_000m);
        result.Single(a => a.Installment.Id.StartsWith("LINE-")).Installment.AmountPaid.Should().Be(5_000m);
        result.Sum(a => a.Installment.AmountPaid).Should().Be(20_000m, "aucun franc n'apparaît ni ne disparaît");
    }

    [Fact]
    public void Money_Is_Never_Created_Or_Lost()
    {
        var result = TaggedInstallmentAllocator.Allocate(
            Schedule(70_000m), 70_000m, new Dictionary<int, decimal> { [6] = 15_000m, [3] = 7_000m }, Today, false);

        result.Sum(a => a.Installment.AmountPaid).Should().Be(70_000m);
        result.Should().OnlyContain(a => a.Installment.AmountPaid <= a.Installment.Amount && a.Installment.RemainingDue >= 0);
    }

    [Fact]
    public void A_Tagged_Month_Never_Lands_On_A_One_Off_Fee()
    {
        // Le mois d'octobre est aussi celui de l'échéance de l'inscription (début d'année) : seule la mensualité le reçoit.
        var result = TaggedInstallmentAllocator.Allocate(
            Schedule(10_000m), 10_000m, new Dictionary<int, decimal> { [10] = 10_000m }, Today, false);

        PaidFor(result, 10).Should().Be(10_000m);
        result.Single(a => a.Installment.Id.StartsWith("LINE-")).Installment.AmountPaid.Should().Be(0m);
    }

    [Fact]
    public void Tagged_Money_Wins_Over_The_Last_Month_Practice_For_The_Rest()
    {
        // Pratique active : le non-désigné va d'abord sur juin. Mais ici juin est désigné à 15 000 : le reste (25 000)
        // remplit l'inscription puis octobre (juin est déjà soldé, la pratique ne lui redonne rien).
        var result = TaggedInstallmentAllocator.Allocate(
            Schedule(40_000m), 40_000m, new Dictionary<int, decimal> { [6] = 15_000m }, Today, firstMonthCoversLast: true);

        PaidFor(result, 6).Should().Be(15_000m);
        PaidFor(result, 10).Should().Be(15_000m);
        result.Single(a => a.Installment.Id.StartsWith("LINE-")).Installment.AmountPaid.Should().Be(10_000m);
    }
}
