using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>Statut d'une sortie (spec N6) : jamais stocké, calculé — et le même verdict en SQL (prédicat d'expression).</summary>
public class BoardingLeaveStatusTests
{
    private static readonly DateOnly Leave = new(2026, 10, 10);
    private static readonly DateOnly Expected = new(2026, 10, 12);

    public static TheoryData<int, int?, LeaveStatus> Cases => new()
    {
        { 9, null, LeaveStatus.Pending },
        { 10, null, LeaveStatus.Active },      // borne basse
        { 12, null, LeaveStatus.Active },      // borne haute
        { 13, null, LeaveStatus.Overdue },
        { 13, 11, LeaveStatus.Returned },      // le retour prime sur le retard
    };

    private static BoardingLeave LeaveOf(int? returnedDay) => new()
    {
        LeaveDate = Leave, ExpectedReturnDate = Expected, AccompaniedBy = "Parent",
        ActualReturnDate = returnedDay is { } d ? new DateOnly(2026, 10, d) : null
    };

    [Theory, MemberData(nameof(Cases))]
    public void Of_Computes_The_Four_States_And_Their_Bounds(int todayDay, int? returnedDay, LeaveStatus expected) =>
        BoardingLeaveStatus.Of(LeaveOf(returnedDay), new DateOnly(2026, 10, todayDay)).Should().Be(expected);

    [Theory, MemberData(nameof(Cases))]
    public void The_Sql_Predicate_Agrees_With_The_Pure_Function(int todayDay, int? returnedDay, LeaveStatus expected)
    {
        var today = new DateOnly(2026, 10, todayDay);
        var leave = LeaveOf(returnedDay);

        foreach (var status in Enum.GetValues<LeaveStatus>())
        {
            BoardingLeaveStatus.Matches(status, today).Compile()(leave).Should().Be(status == expected, $"statut testé : {status}");
        }
    }

    [Theory]
    [InlineData(10, null, true)]
    [InlineData(20, null, true)]    // sortie dépassée mais non rentrée : il dort toujours dehors
    [InlineData(9, null, false)]    // pas encore parti
    [InlineData(11, 12, true)]
    [InlineData(12, 12, false)]     // nuit du retour : il dort à l'internat
    [InlineData(13, 12, false)]
    public void OnNight_Means_Away_And_Not_Yet_Back_That_Night(int nightDay, int? returnedDay, bool expected) =>
        BoardingLeaveStatus.OnNight(new DateOnly(2026, 10, nightDay)).Compile()(LeaveOf(returnedDay)).Should().Be(expected);
}
