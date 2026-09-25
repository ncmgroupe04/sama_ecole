using FluentAssertions;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Common.Exceptions;
using Xunit;

namespace SamaEcole.UnitTests.Attendance;

/// <summary>
/// Complément N°5 bis, arbitrage C3 — à partir de l'heure d'arrivée d'un élève, quels cours sont MANQUÉS, lequel est
/// EN COURS (retard en minutes), et quelle est la durée totale. Trois cours le samedi : 08:00-10:00 (1),
/// 10:00-12:00 (2), 14:00-16:00 (3).
/// </summary>
public class ArrivalCoverageTests
{
    private static readonly Guid Cours1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Cours2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Cours3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static IReadOnlyList<ArrivalSlot> Journee() =>
    [
        new(Cours1, new TimeOnly(8, 0), new TimeOnly(10, 0)),
        new(Cours2, new TimeOnly(10, 0), new TimeOnly(12, 0)),
        new(Cours3, new TimeOnly(14, 0), new TimeOnly(16, 0))
    ];

    // 1 — retard en minutes sur le cours en cours (08h15 pour le cours de 08h00).
    [Fact]
    public void Arriving_Quarter_Past_Eight_Is_A_Fifteen_Minute_Late_On_The_First_Course()
    {
        var result = ArrivalCoverage.Compute(Journee(), new TimeOnly(8, 15));

        result.MissedSlotIds.Should().BeEmpty();
        result.InProgressSlotId.Should().Be(Cours1);
        result.LateMinutes.Should().Be(15);
        result.MissedMinutes.Should().Be(0);
        result.TotalMinutes.Should().Be(15);
        result.TargetSlotId.Should().Be(Cours1);
    }

    // 2 — absent 08h-10h, présent à 10h : le premier cours est manqué, aucun retard sur le second.
    [Fact]
    public void Arriving_Exactly_When_The_Second_Course_Starts_Misses_The_First_Without_Any_Late()
    {
        var result = ArrivalCoverage.Compute(Journee(), new TimeOnly(10, 0));

        result.MissedSlotIds.Should().Equal(Cours1);
        result.InProgressSlotId.Should().BeNull();
        result.LateMinutes.Should().Be(0);
        result.MissedMinutes.Should().Be(120);
        result.TotalMinutes.Should().Be(120);
        result.TargetSlotId.Should().Be(Cours2);
    }

    // 3 — les deux à la fois : cours manqué + retard sur le suivant.
    [Fact]
    public void Arriving_Twenty_Minutes_Into_The_Second_Course_Misses_The_First_And_Is_Late_For_The_Second()
    {
        var result = ArrivalCoverage.Compute(Journee(), new TimeOnly(10, 20));

        result.MissedSlotIds.Should().Equal(Cours1);
        result.InProgressSlotId.Should().Be(Cours2);
        result.LateMinutes.Should().Be(20);
        result.MissedMinutes.Should().Be(120);
        result.TotalMinutes.Should().Be(140);
        result.TargetSlotId.Should().Be(Cours2);
    }

    // 4 — arrivée pendant une pause : tout ce qui est terminé est manqué, le billet vise le PROCHAIN cours (C4).
    [Fact]
    public void Arriving_During_A_Break_Misses_Whats_Over_And_Targets_The_Next_Course()
    {
        var result = ArrivalCoverage.Compute(Journee(), new TimeOnly(12, 30));

        result.MissedSlotIds.Should().Equal(Cours1, Cours2);
        result.InProgressSlotId.Should().BeNull();
        result.LateMinutes.Should().Be(0);
        result.TotalMinutes.Should().Be(240);
        result.TargetSlotId.Should().Be(Cours3);
    }

    // 5 — plus aucun cours : tout est manqué et le billet vise le DERNIER cours manqué (C4).
    [Fact]
    public void Arriving_After_The_Last_Course_Misses_Everything_And_Targets_The_Last_Missed_Course()
    {
        var result = ArrivalCoverage.Compute(Journee(), new TimeOnly(17, 0));

        result.MissedSlotIds.Should().Equal(Cours1, Cours2, Cours3);
        result.InProgressSlotId.Should().BeNull();
        result.TotalMinutes.Should().Be(360);
        result.TargetSlotId.Should().Be(Cours3);
    }

    // 6 — avant le premier cours : rien n'a commencé.
    [Fact]
    public void Arriving_Before_The_First_Course_Is_Refused_With_The_First_Start_Time()
    {
        var act = () => ArrivalCoverage.Compute(Journee(), new TimeOnly(7, 30));

        act.Should().Throw<ValidationException>().Which.Errors["ArrivalTime"].Single()
            .Should().Contain("08:00");
    }

    // 7 — pile à l'heure du premier cours : rien à régulariser.
    [Fact]
    public void Arriving_Exactly_When_The_First_Course_Starts_Has_Nothing_To_Regularise()
    {
        var act = () => ArrivalCoverage.Compute(Journee(), new TimeOnly(8, 0));

        act.Should().Throw<ValidationException>().Which.Errors["ArrivalTime"].Single()
            .Should().Contain("Rien à régulariser");
    }

    // 8 — aucun cours ce jour-là.
    [Fact]
    public void A_Class_Without_Any_Course_That_Day_Is_Refused()
    {
        var act = () => ArrivalCoverage.Compute([], new TimeOnly(9, 0));

        act.Should().Throw<ValidationException>().Which.Errors["ArrivalTime"].Single()
            .Should().Contain("Aucun cours");
    }

    // 9 — l'ordre des créneaux en entrée est indifférent.
    [Fact]
    public void The_Order_Of_The_Slots_Does_Not_Matter()
    {
        var shuffled = Journee().Reverse().ToList();

        var result = ArrivalCoverage.Compute(shuffled, new TimeOnly(10, 20));

        result.MissedSlotIds.Should().Equal(Cours1);
        result.InProgressSlotId.Should().Be(Cours2);
        result.TotalMinutes.Should().Be(140);
    }
}
