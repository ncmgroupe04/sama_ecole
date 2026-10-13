using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// Point d'application unique du quota d'élèves : refus 422 (QuotaExceededException), avertissement renvoyé
/// quand le plafond nominal est dépassé, et prise en compte d'un import de plusieurs élèves d'un coup.
/// </summary>
public class StudentQuotaGuardTests
{
    private static StudentQuotaGuard GuardFor(
        int current, int max, int soft, TenantSubscriptionStatus? status = TenantSubscriptionStatus.Active, RecordingTracker? tracker = null) =>
        new(new FixedQuotaService(current, max, soft, status), tracker ?? new RecordingTracker());

    [Fact]
    public async Task Within_The_Nominal_Limit_Nothing_Is_Raised_And_No_Warning_Is_Returned()
    {
        var warning = await GuardFor(current: 10, max: 150, soft: 160).EnsureCanAddAsync(1, CancellationToken.None);

        warning.Should().BeNull();
    }

    [Fact]
    public async Task Reaching_The_Nominal_Limit_Exactly_Is_Still_Silent()
    {
        // 149 + 1 = 150 : le plafond nominal est atteint, pas dépassé.
        var warning = await GuardFor(current: 149, max: 150, soft: 160).EnsureCanAddAsync(1, CancellationToken.None);

        warning.Should().BeNull();
    }

    [Fact]
    public async Task Crossing_The_Nominal_Limit_Returns_A_Warning_With_The_Remaining_Room()
    {
        var warning = await GuardFor(current: 150, max: 150, soft: 160).EnsureCanAddAsync(1, CancellationToken.None);

        warning.Should().Be(new StudentQuotaWarning(
            CurrentStudentCount: 151, MaxStudentLimit: 150, SoftQuotaLimit: 160, RemainingBeforeBlock: 9));
    }

    [Fact]
    public async Task The_Last_Student_Allowed_By_The_Tolerance_Is_Admitted_With_No_Room_Left()
    {
        var warning = await GuardFor(current: 159, max: 150, soft: 160).EnsureCanAddAsync(1, CancellationToken.None);

        warning.Should().NotBeNull();
        warning!.RemainingBeforeBlock.Should().Be(0);
    }

    [Fact]
    public async Task Past_The_Soft_Cap_Student_Creation_Is_Refused_With_The_Figures()
    {
        var act = async () => await GuardFor(current: 160, max: 150, soft: 160).EnsureCanAddAsync(1, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<QuotaExceededException>()).Which;
        ex.Denial.Should().Be(StudentAdmissionDenial.QuotaExceeded);
        ex.Quota.CurrentStudentCount.Should().Be(160);
        ex.Quota.SoftQuotaLimit.Should().Be(160);
        ex.Message.Should().Contain("160").And.Contain("150");
    }

    [Fact]
    public async Task An_Import_That_Would_Cross_The_Soft_Cap_Is_Refused_As_A_Whole()
    {
        // 140 présents + 25 du fichier = 165 > 160, même si chaque élève pris isolément passerait.
        var act = async () => await GuardFor(current: 140, max: 150, soft: 160).EnsureCanAddAsync(25, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<QuotaExceededException>()).Which;
        ex.Message.Should().Contain("25");
    }

    [Fact]
    public async Task An_Import_That_Fits_Under_The_Soft_Cap_Is_Admitted_With_A_Warning()
    {
        var warning = await GuardFor(current: 140, max: 150, soft: 160).EnsureCanAddAsync(15, CancellationToken.None);

        warning.Should().Be(new StudentQuotaWarning(155, 150, 160, 5));
    }

    [Theory]
    [InlineData(TenantSubscriptionStatus.PendingOnboarding)]
    [InlineData(TenantSubscriptionStatus.PendingApproval)]
    [InlineData(TenantSubscriptionStatus.Suspended)]
    [InlineData(TenantSubscriptionStatus.Expired)]
    public async Task A_Subscription_That_Is_Not_Active_Refuses_Creation_Whatever_The_Headcount(TenantSubscriptionStatus status)
    {
        var act = async () => await GuardFor(current: 0, max: 150, soft: 160, status).EnsureCanAddAsync(1, CancellationToken.None);

        (await act.Should().ThrowAsync<QuotaExceededException>()).Which.Denial
            .Should().Be(StudentAdmissionDenial.SubscriptionNotActive);
    }

    [Fact]
    public async Task A_School_Without_A_Subscription_Is_Refused()
    {
        var act = async () => await GuardFor(current: 0, max: 0, soft: 0, status: null).EnsureCanAddAsync(1, CancellationToken.None);

        (await act.Should().ThrowAsync<QuotaExceededException>()).Which.Denial
            .Should().Be(StudentAdmissionDenial.SubscriptionNotActive);
    }

    [Fact]
    public async Task An_Unlimited_School_Is_Never_Warned_Nor_Refused()
    {
        var warning = await GuardFor(current: 50_000, max: int.MaxValue, soft: int.MaxValue).EnsureCanAddAsync(1000, CancellationToken.None);

        warning.Should().BeNull();
    }

    // ------------------------------------------------------------------ Date du premier dépassement (OverQuotaSince)

    [Fact]
    public async Task Every_Evaluation_Records_The_Quota_State_So_That_The_First_Overrun_Is_Dated()
    {
        var tracker = new RecordingTracker();

        await GuardFor(current: 155, max: 150, soft: 160, tracker: tracker).EnsureCanAddAsync(1, CancellationToken.None);

        tracker.States.Should().Equal(StudentQuotaState.InTolerance);
    }

    [Fact]
    public async Task The_State_Is_Recorded_Even_When_The_Creation_Is_Refused()
    {
        var tracker = new RecordingTracker();

        var act = async () => await GuardFor(current: 170, max: 150, soft: 160, tracker: tracker).EnsureCanAddAsync(1, CancellationToken.None);

        await act.Should().ThrowAsync<QuotaExceededException>();
        tracker.States.Should().Equal(StudentQuotaState.Exceeded);
    }

    [Fact]
    public async Task A_School_Within_Its_Quota_Is_Recorded_As_Such_So_That_The_Marker_Is_Cleared()
    {
        var tracker = new RecordingTracker();

        await GuardFor(current: 40, max: 150, soft: 160, tracker: tracker).EnsureCanAddAsync(1, CancellationToken.None);

        tracker.States.Should().Equal(StudentQuotaState.WithinQuota);
    }

    private sealed class RecordingTracker : IOverQuotaTracker
    {
        public List<StudentQuotaState> States { get; } = [];

        public Task RecordAsync(StudentQuotaStatus quota, CancellationToken cancellationToken)
        {
            States.Add(quota.State);
            return Task.CompletedTask;
        }
    }

    /// <summary>Service figé : le verdict vient de <see cref="StudentQuotaEvaluator"/>, la vraie règle.</summary>
    private sealed class FixedQuotaService(int current, int max, int soft, TenantSubscriptionStatus? status) : ITenantSubscriptionService
    {
        public Task<TenantSubscriptionDto?> GetCurrentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsModuleEnabledAsync(SchoolModule module, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<StudentQuotaStatus> GetQuotaStatusAsync(int additionalStudents, CancellationToken cancellationToken) =>
            Task.FromResult(status is null
                ? StudentQuotaEvaluator.WithoutSubscription(current)
                : StudentQuotaEvaluator.Evaluate(current, max, soft, status, additionalStudents));
    }
}
