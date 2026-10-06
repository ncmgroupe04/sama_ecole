using FluentAssertions;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// Règle de quota d'élèves (plafond nominal + tolérance « Soft cap ») et plafonds par tranche.
/// </summary>
public class StudentQuotaEvaluatorTests
{
    [Theory]
    [InlineData(StudentQuotaTier.Tier1_150, 150, 160)]
    [InlineData(StudentQuotaTier.Tier2_400, 400, 420)]
    [InlineData(StudentQuotaTier.Tier3_800, 800, 830)]
    [InlineData(StudentQuotaTier.Tier4_Custom, int.MaxValue, int.MaxValue)]
    public void Each_Tier_Has_Its_Documented_Limits(StudentQuotaTier tier, int max, int soft)
    {
        StudentQuotaDefaults.For(tier).Should().Be((max, soft));
    }

    [Fact]
    public void Every_Tier_Keeps_Soft_Limit_At_Or_Above_Max_Limit()
    {
        foreach (var tier in Enum.GetValues<StudentQuotaTier>())
        {
            var (max, soft) = StudentQuotaDefaults.For(tier);
            soft.Should().BeGreaterThanOrEqualTo(max, $"la tranche {tier} violerait CK_tenant_subscriptions_soft_gte_max");
        }
    }

    [Theory]
    [InlineData(100, 110)]   // marge plancher : 4 % de 100 = 4 < 10
    [InlineData(1000, 1040)] // 4 % de 1000 = 40 > 10
    [InlineData(int.MaxValue - 5, int.MaxValue)] // jamais de débordement
    public void A_Custom_Limit_Gets_A_Computed_Tolerance_That_Never_Overflows(int max, int expectedSoft)
    {
        StudentQuotaDefaults.ForCustom(max).Should().Be((max, expectedSoft));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_Custom_Limit_Below_One_Is_Rejected(int max)
    {
        var act = () => StudentQuotaDefaults.ForCustom(max);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(149, true)]
    [InlineData(150, true)]   // atteint le plafond : le 151e entre en tolérance, admis
    [InlineData(159, true)]   // le 160e est encore admis (Soft cap inclus)
    [InlineData(160, false)]  // le 161e est refusé
    [InlineData(200, false)]
    public void Tier_1_Admits_Students_Up_To_The_Soft_Cap_Included(int current, bool canAdd)
    {
        var status = StudentQuotaEvaluator.Evaluate(current, 150, 160, TenantSubscriptionStatus.Active);

        status.CanAddStudent.Should().Be(canAdd);
        status.Denial.Should().Be(canAdd ? StudentAdmissionDenial.None : StudentAdmissionDenial.QuotaExceeded);
    }

    [Theory]
    [InlineData(150, StudentQuotaState.WithinQuota)]
    [InlineData(151, StudentQuotaState.InTolerance)]
    [InlineData(160, StudentQuotaState.InTolerance)]
    [InlineData(161, StudentQuotaState.Exceeded)]
    public void State_Reports_Where_The_Current_Headcount_Stands(int current, StudentQuotaState expected)
    {
        StudentQuotaEvaluator.Evaluate(current, 150, 160, TenantSubscriptionStatus.Active).State.Should().Be(expected);
    }

    [Theory]
    [InlineData(TenantSubscriptionStatus.PendingOnboarding)]
    [InlineData(TenantSubscriptionStatus.PendingApproval)]
    [InlineData(TenantSubscriptionStatus.Suspended)]
    [InlineData(TenantSubscriptionStatus.Expired)]
    public void No_Student_Can_Be_Added_Unless_The_Subscription_Is_Active(TenantSubscriptionStatus status)
    {
        var verdict = StudentQuotaEvaluator.Evaluate(0, 150, 160, status);

        verdict.CanAddStudent.Should().BeFalse();
        verdict.Denial.Should().Be(StudentAdmissionDenial.SubscriptionNotActive);
    }

    [Fact]
    public void An_Unlimited_Subscription_Never_Overflows_And_Always_Admits()
    {
        var verdict = StudentQuotaEvaluator.Evaluate(int.MaxValue - 1, int.MaxValue, int.MaxValue, TenantSubscriptionStatus.Active);

        verdict.CanAddStudent.Should().BeTrue();
        verdict.State.Should().Be(StudentQuotaState.WithinQuota);

        // Au plafond exact : +1 dépasserait int.MaxValue, mais le calcul se fait en long.
        StudentQuotaEvaluator.Evaluate(int.MaxValue, int.MaxValue, int.MaxValue, TenantSubscriptionStatus.Active)
            .CanAddStudent.Should().BeFalse();
    }

    [Fact]
    public void Without_A_Subscription_Everything_Is_Refused()
    {
        var verdict = StudentQuotaEvaluator.WithoutSubscription(12);

        verdict.CanAddStudent.Should().BeFalse();
        verdict.Denial.Should().Be(StudentAdmissionDenial.SubscriptionNotActive);
        verdict.CurrentStudentCount.Should().Be(12);
    }
}
