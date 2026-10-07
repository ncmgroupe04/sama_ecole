using FluentAssertions;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Boarders.GetBoarder;
using SamaEcole.Application.Boarding.Boarders.ListBoarders;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Application.Boarding.Leaves.ListLeaves;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Liste des sorties, sorties de la fiche pensionnaire et filtre « en permission » (lot D, tâche 4). Aujourd'hui = 12/10.</summary>
[Trait("Category", "MultiTenant")]
public class LeaveQueriesTests : LeaveTestBase
{
    private readonly Dictionary<string, Guid> _leaves = new();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var owner = Db.NewOwnerContext();

        Add(owner, "m1-actif", "m1", new(2026, 10, 10), new(2026, 10, 14), null);
        Add(owner, "m1-ancienne", "m1", new(2026, 9, 20), new(2026, 9, 22), new DateOnly(2026, 9, 22), unlisted: true);
        Add(owner, "m2-retard", "m2", new(2026, 10, 5), new(2026, 10, 8), null);
        Add(owner, "m3-a-venir", "m3", new(2026, 10, 15), new(2026, 10, 16), null);
        Add(owner, "m4-rentree", "m4", new(2026, 10, 1), new(2026, 10, 3), new DateOnly(2026, 10, 3));
        await owner.SaveChangesAsync();
    }

    private void Add(
        SamaEcole.Persistence.ApplicationDbContext owner, string key, string stay, DateOnly leave, DateOnly expected,
        DateOnly? returned, bool unlisted = false)
    {
        var entity = new BoardingLeave
        {
            SchoolId = EcoleA, BoardingEnrollmentId = Stays[stay], LeaveDate = leave, ExpectedReturnDate = expected,
            ActualReturnDate = returned, Reason = BoardingLeaveReason.Weekend, AccompaniedBy = "Parent", IsCompanionUnlisted = unlisted
        };
        _leaves[key] = entity.Id;
        owner.BoardingLeaves.Add(entity);
    }

    private async Task<PaginatedLeaves> ListAsync(ListLeavesQuery query, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new ListLeavesQueryHandler(ctx, Clock).Handle(query, CancellationToken.None);
    }

    [Theory]
    [InlineData(LeaveStatus.Pending, "m3-a-venir")]
    [InlineData(LeaveStatus.Active, "m1-actif")]
    [InlineData(LeaveStatus.Overdue, "m2-retard")]
    [InlineData(LeaveStatus.Returned, "m1-ancienne", "m4-rentree")]
    public async Task The_Status_Filter_Returns_Exactly_The_Leaves_In_That_State(LeaveStatus status, params string[] expectedKeys)
    {
        var result = await ListAsync(new ListLeavesQuery { Status = status });

        result.Items.Select(i => i.Id).Should().BeEquivalentTo(expectedKeys.Select(k => _leaves[k]));
        result.Items.Should().OnlyContain(i => i.Status == status, "le statut affiché est celui de la fonction pure");
    }

    [Fact]
    public async Task Without_A_Filter_Every_Leave_Is_Listed_Newest_First_With_Its_Computed_Status()
    {
        var result = await ListAsync(new ListLeavesQuery());

        result.TotalCount.Should().Be(5);
        result.Items.Select(i => i.Id).Should().Equal(
            _leaves["m3-a-venir"], _leaves["m1-actif"], _leaves["m2-retard"], _leaves["m4-rentree"], _leaves["m1-ancienne"]);
        result.Items.Single(i => i.Id == _leaves["m1-actif"]).Should().Match<BoardingLeaveDto>(
            i => i.StudentName == "Élève m1" && i.DormitoryName == "Pavillon Oustaz Ahmad" && i.RoomName == "Chambre 101"
                 && i.Status == LeaveStatus.Active);
    }

    [Fact]
    public async Task Boarder_Dormitory_Overlap_And_Unlisted_Filters_Narrow_The_List()
    {
        (await ListAsync(new ListLeavesQuery { BoarderId = Stays["m1"] })).Items.Should().HaveCount(2);
        (await ListAsync(new ListLeavesQuery { DormitoryId = PavA })).Items.Should().HaveCount(4);
        (await ListAsync(new ListLeavesQuery { DormitoryId = PavA2 })).Items.Select(i => i.Id).Should().Equal(_leaves["m4-rentree"]);

        // Chevauchement de [sortie, retour prévu] avec [09/10, 11/10] : seule la sortie active de m1 (10→14/10).
        (await ListAsync(new ListLeavesQuery { From = new DateOnly(2026, 10, 9), To = new DateOnly(2026, 10, 11) }))
            .Items.Select(i => i.Id).Should().Equal(_leaves["m1-actif"]);

        (await ListAsync(new ListLeavesQuery { UnlistedOnly = true })).Items.Select(i => i.Id).Should().Equal(_leaves["m1-ancienne"]);
    }

    [Fact]
    public async Task Pagination_Is_Applied_And_The_Page_Size_Is_Capped()
    {
        var second = await ListAsync(new ListLeavesQuery { Page = 2, PageSize = 2 });
        second.Items.Select(i => i.Id).Should().Equal(_leaves["m2-retard"], _leaves["m4-rentree"]);
        second.TotalCount.Should().Be(5);

        (await ListAsync(new ListLeavesQuery { PageSize = 1000 })).PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Another_School_Sees_No_Leave()
    {
        var result = await ListAsync(new ListLeavesQuery(), school: EcoleB);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task The_Boarder_Sheet_Carries_The_Latest_Leaves_Newest_First()
    {
        await using var ctx = Db.NewAppContext(EcoleA);
        var detail = await new GetBoarderQueryHandler(ctx, new TestCurrentUser(Guid.NewGuid(), Role.Surveillant), Clock)
            .Handle(new GetBoarderQuery(Stays["m1"]), CancellationToken.None);

        detail.Leaves.Select(l => l.Id).Should().Equal(_leaves["m1-actif"], _leaves["m1-ancienne"]);
        detail.Leaves.Should().OnlyContain(l => l.BoarderId == Stays["m1"]);
    }

    [Fact]
    public async Task The_OnLeave_Filter_Lists_Boarders_Currently_Away_Including_Overdue_But_Not_Pending_Or_Returned()
    {
        await using var ctx = Db.NewAppContext(EcoleA);
        var result = await new ListBoardersQueryHandler(ctx, Clock)
            .Handle(new ListBoardersQuery { Status = "onLeave" }, CancellationToken.None);

        result.Items.Select(i => i.Id).Should().BeEquivalentTo([Stays["m1"], Stays["m2"]]);
    }
}
