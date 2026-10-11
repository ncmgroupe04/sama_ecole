using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Boarding.Boarders.EndBoarding;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Application.Boarding.Leaves.DeclareLeave;
using SamaEcole.Application.Boarding.Leaves.RecordReturn;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Enregistrement du retour d'une sortie (lot D, tâche 3) — handler + PostgreSQL réel.</summary>
[Trait("Category", "MultiTenant")]
public class LeaveReturnTests : LeaveTestBase
{
    private async Task<BoardingLeaveDto> DeclareAsync(string key, int leaveDay = 10, int expectedDay = 14)
    {
        await using var ctx = Db.NewAppContext(EcoleA);
        return await new DeclareLeaveCommandHandler(ctx, Clock).Handle(
            new DeclareLeaveCommand(
                Stays[key], new DateOnly(2026, 10, leaveDay), new DateOnly(2026, 10, expectedDay),
                BoardingLeaveReason.Famille, null, "Fatou Diop"),
            CancellationToken.None);
    }

    private async Task<BoardingLeaveDto> ReturnAsync(Guid leaveId, uint rowVersion, DateOnly? date = null, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new RecordReturnCommandHandler(ctx, Clock)
            .Handle(new RecordReturnCommand(leaveId, rowVersion, date), CancellationToken.None);
    }

    [Fact]
    public async Task Returning_Without_A_Date_Uses_Today_And_Closes_The_Leave()
    {
        var leave = await DeclareAsync("m1");

        var result = await ReturnAsync(leave.Id, leave.RowVersion);

        result.Status.Should().Be(LeaveStatus.Returned);
        result.ActualReturnDate.Should().Be(Today);
        result.RowVersion.Should().NotBe(leave.RowVersion, "l'écriture change le jeton de concurrence");
    }

    [Fact]
    public async Task An_Explicit_Past_Date_Is_Accepted_But_Not_Before_The_Leave_Nor_In_The_Future()
    {
        var leave = await DeclareAsync("m2", leaveDay: 10);

        var tooEarly = async () => await ReturnAsync(leave.Id, leave.RowVersion, new DateOnly(2026, 10, 9));
        var future = async () => await ReturnAsync(leave.Id, leave.RowVersion, new DateOnly(2026, 10, 13));

        await tooEarly.Should().ThrowAsync<ValidationException>();
        await future.Should().ThrowAsync<ValidationException>();

        var ok = await ReturnAsync(leave.Id, leave.RowVersion, new DateOnly(2026, 10, 11));
        ok.ActualReturnDate.Should().Be(new DateOnly(2026, 10, 11));
    }

    [Fact]
    public async Task Returning_Twice_Is_Refused_With_A_Clear_Code()
    {
        var leave = await DeclareAsync("m3");
        var returned = await ReturnAsync(leave.Id, leave.RowVersion);

        var again = async () => await ReturnAsync(leave.Id, returned.RowVersion);

        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LEAVE_ALREADY_RETURNED");
    }

    [Fact]
    public async Task Two_Simultaneous_Returns_With_The_Same_Token_Yield_One_Success_And_One_Clean_Conflict()
    {
        var leave = await DeclareAsync("m1");

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(async _ =>
            {
                try
                {
                    await ReturnAsync(leave.Id, leave.RowVersion);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }));

        attempts.Count(e => e is null).Should().Be(1);
        // Le perdant arrive soit avant la validation de l'autre (jeton périmé), soit après (déjà rentré) : jamais une 500.
        attempts.Single(e => e is not null).Should().Match<Exception>(e =>
            e is ConcurrencyConflictException
            || (e is BusinessRuleException && ((BusinessRuleException)e).Code == "LEAVE_ALREADY_RETURNED"));
    }

    [Fact]
    public async Task A_Stale_Token_Is_Rejected()
    {
        var leave = await DeclareAsync("m2");

        var stale = async () => await ReturnAsync(leave.Id, leave.RowVersion + 1);

        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task A_Leave_Of_Another_School_Or_Unknown_Is_Not_Found()
    {
        var leave = await DeclareAsync("m3");

        var foreign = async () => await ReturnAsync(leave.Id, leave.RowVersion, school: EcoleB);
        var unknown = async () => await ReturnAsync(Guid.NewGuid(), 1);

        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_Stay_Cannot_Be_Ended_While_The_Leave_Is_Open_But_Can_After_The_Return()
    {
        var leave = await DeclareAsync("m1");

        async Task EndAsync()
        {
            await using var ctx = Db.NewAppContext(EcoleA);
            var token = await ctx.BoardingEnrollments.Where(b => b.Id == Stays["m1"])
                .Select(b => EF.Property<uint>(b, "xmin")).SingleAsync();
            await new EndBoardingCommandHandler(ctx, new BoardingAssignmentService(ctx, Clock))
                .Handle(new EndBoardingCommand(Stays["m1"], token), CancellationToken.None);
        }

        var whileOpen = async () => await EndAsync();
        (await whileOpen.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LEAVE_IN_PROGRESS");

        await ReturnAsync(leave.Id, leave.RowVersion);

        await EndAsync();
        await using var check = Db.NewOwnerContext();
        (await check.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == Stays["m1"])).IsActive.Should().BeFalse();
    }
}
