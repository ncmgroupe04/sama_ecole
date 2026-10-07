using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Application.Boarding.Leaves.DeclareLeave;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Déclaration d'une sortie (lot D, tâche 2) — handler + PostgreSQL réel, HTTP exclu.</summary>
[Trait("Category", "MultiTenant")]
public class LeaveCommandsTests : LeaveTestBase
{
    private async Task<BoardingLeaveDto> DeclareAsync(
        Guid boarderId, string companion = "Fatou Diop", int leaveDay = 10, int expectedDay = 14, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new DeclareLeaveCommandHandler(ctx, Clock).Handle(
            new DeclareLeaveCommand(
                boarderId, new DateOnly(2026, 10, leaveDay), new DateOnly(2026, 10, expectedDay),
                BoardingLeaveReason.Weekend, null, companion),
            CancellationToken.None);
    }

    [Fact]
    public async Task Declaring_A_Leave_Persists_It_And_Computes_Its_Status()
    {
        var result = await DeclareAsync(Stays["m1"], companion: "fatou  DIOP");

        result.BoarderId.Should().Be(Stays["m1"]);
        result.Status.Should().Be(LeaveStatus.Active, "aujourd'hui (12/10) est entre la sortie (10/10) et le retour prévu (14/10)");
        result.IsCompanionUnlisted.Should().BeFalse();
        result.ActualReturnDate.Should().BeNull();
        result.AccompaniedBy.Should().Be("fatou  DIOP");
        (result.StudentName, result.Matricule).Should().Be(("Élève m1", "M-m1"));
        (result.DormitoryName, result.RoomName).Should().Be(("Pavillon Oustaz Ahmad", "Chambre 101"));
    }

    [Fact]
    public async Task A_Leave_Starting_In_The_Future_Is_Pending()
    {
        var result = await DeclareAsync(Stays["m2"], leaveDay: 15, expectedDay: 16);

        result.Status.Should().Be(LeaveStatus.Pending);
    }

    [Fact]
    public async Task An_Unlisted_Companion_Is_Accepted_Flagged_And_The_Flag_Is_Frozen()
    {
        var result = await DeclareAsync(Stays["m1"], companion: "Oncle Moussa");

        result.IsCompanionUnlisted.Should().BeTrue("décision Q5 : accepté, jamais bloqué, mais tracé");

        // Habiliter l'accompagnateur APRÈS coup ne réécrit pas l'historique.
        await using (var owner = Db.NewOwnerContext())
        {
            var stay = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == Stays["m1"]);
            stay.AllowedExitPersons = [new AllowedExitPerson { Name = "Oncle Moussa", Relationship = "Oncle", Phone = "771111111" }];
            await owner.SaveChangesAsync();
        }

        await using var check = Db.NewOwnerContext();
        (await check.BoardingLeaves.IgnoreQueryFilters().SingleAsync(l => l.Id == result.Id)).IsCompanionUnlisted.Should().BeTrue();
    }

    [Fact]
    public async Task A_Second_Open_Leave_Is_Refused()
    {
        await DeclareAsync(Stays["m2"]);

        var again = async () => await DeclareAsync(Stays["m2"], leaveDay: 11, expectedDay: 13);

        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("LEAVE_ALREADY_OPEN");
    }

    [Fact]
    public async Task Concurrent_Declarations_Yield_One_Leave_And_One_Conflict_Never_A_Raw_Error()
    {
        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(async _ =>
            {
                try
                {
                    await DeclareAsync(Stays["m3"]);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }));

        attempts.Count(e => e is null).Should().Be(1);
        attempts.Single(e => e is not null).Should().BeOfType<BusinessRuleException>()
            .Which.Code.Should().Be("LEAVE_ALREADY_OPEN");

        await using var check = Db.NewOwnerContext();
        (await check.BoardingLeaves.IgnoreQueryFilters().CountAsync(l => l.BoardingEnrollmentId == Stays["m3"])).Should().Be(1);
    }

    [Fact]
    public async Task An_Ended_Stay_Or_Another_Schools_Stay_Is_Not_Found()
    {
        var ended = async () => await DeclareAsync(Stays["clos"]);
        var foreign = async () => await DeclareAsync(Stays["autre"]);          // tenant École A, séjour de l'École B
        var unknown = async () => await DeclareAsync(Guid.NewGuid());

        await ended.Should().ThrowAsync<KeyNotFoundException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }
}
