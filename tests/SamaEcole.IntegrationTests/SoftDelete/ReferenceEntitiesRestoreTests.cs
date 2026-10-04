using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Classrooms.Commands.RestoreClassroom;
using SamaEcole.Application.Classrooms.Queries.GetDeletedClassrooms;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.RestoreFeeCategory;
using SamaEcole.Application.Finance.Queries.GetDeletedFeeCategories;
using SamaEcole.Application.Grades.Commands.CreateMention;
using SamaEcole.Application.Grades.Commands.RestoreMention;
using SamaEcole.Application.Grades.Queries.GetDeletedMentions;
using SamaEcole.Application.Rooms.Commands.CreateRoom;
using SamaEcole.Application.Rooms.Commands.RestoreRoom;
using SamaEcole.Application.Rooms.Queries.GetDeletedRooms;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Conception soft delete 2026-10-01 §3.2 pour Classroom, Room, FeeCategory et Mention : création bloquée par
/// <c>ARCHIVED_ENTITY_EXISTS</c>, restauration, <c>ACTIVE_ENTITY_CONFLICT</c>, bâtiment parent supprimé, et
/// isolation tenant de la corbeille et de la restauration (RLS PostgreSQL réelle, rôle applicatif).
/// </summary>
[Trait("Category", "MultiTenant")]
public class ReferenceEntitiesRestoreTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("51111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("52222222-2222-2222-2222-222222222222");
    private static readonly Guid BatimentActif = Guid.Parse("5b000000-0000-0000-0000-000000000001");
    private static readonly Guid BatimentSupprime = Guid.Parse("5b000000-0000-0000-0000-000000000002");

    // Un tombstone par entité dans l'école A ; les identifiants sont dérivés du type.
    private static readonly Dictionary<string, Guid> Tombstones = new()
    {
        ["Classroom"] = Guid.Parse("5c000000-0000-0000-0000-000000000001"),
        ["Room"] = Guid.Parse("5c000000-0000-0000-0000-000000000002"),
        ["FeeCategory"] = Guid.Parse("5c000000-0000-0000-0000-000000000003"),
        ["Mention"] = Guid.Parse("5c000000-0000-0000-0000-000000000004"),
    };

    public static TheoryData<string> Kinds => new() { "Classroom", "Room", "FeeCategory", "Mention" };

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        var batimentSupprime = new Building { Id = BatimentSupprime, SchoolId = EcoleA, Name = "Supprimé" };
        owner.Buildings.AddRange(new Building { Id = BatimentActif, SchoolId = EcoleA, Name = "Actif" }, batimentSupprime);
        await owner.SaveChangesAsync(CancellationToken.None);

        AuditableEntity[] tombstones =
        [
            new Classroom { Id = Tombstones["Classroom"], SchoolId = EcoleA, Name = "6e A", Level = "6e", Capacity = 40, Cycle = CycleType.College },
            new Room { Id = Tombstones["Room"], SchoolId = EcoleA, BuildingId = BatimentActif, Name = "Salle 1", Capacity = 30 },
            new FeeCategory { Id = Tombstones["FeeCategory"], SchoolId = EcoleA, Name = "Cantine" },
            new Mention { Id = Tombstones["Mention"], SchoolId = EcoleA, Label = "Bien", MinAverage = 14 },
        ];
        owner.AddRange(tombstones);
        await owner.SaveChangesAsync(CancellationToken.None);
        foreach (var t in tombstones) t.SoftDelete("test");
        batimentSupprime.SoftDelete("test");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static AuditableEntity MakeActive(string kind, Guid school) => kind switch
    {
        "Classroom" => new Classroom { SchoolId = school, Name = "6e A", Level = "6e", Capacity = 40, Cycle = CycleType.College },
        "Room" => new Room { SchoolId = school, BuildingId = BatimentActif, Name = "Salle 1", Capacity = 30 },
        "FeeCategory" => new FeeCategory { SchoolId = school, Name = "Cantine" },
        "Mention" => new Mention { SchoolId = school, Label = "Bien", MinAverage = 14 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private async Task RestoreAsync(string kind, Guid school, Guid id)
    {
        await using var ctx = _db.NewAppContext(school);
        var tenant = new FixedTenantProvider(school);
        switch (kind)
        {
            case "Classroom": await new RestoreClassroomCommandHandler(ctx, tenant, new NoOpKpiCache()).Handle(new RestoreClassroomCommand(id), default); break;
            case "Room": await new RestoreRoomCommandHandler(ctx, tenant).Handle(new RestoreRoomCommand(id), default); break;
            case "FeeCategory": await new RestoreFeeCategoryCommandHandler(ctx, tenant).Handle(new RestoreFeeCategoryCommand(id), default); break;
            case "Mention": await new RestoreMentionCommandHandler(ctx, tenant).Handle(new RestoreMentionCommand(id), default); break;
        }
    }

    private async Task<IReadOnlyList<Guid>> ListDeletedIdsAsync(string kind, Guid school)
    {
        await using var ctx = _db.NewAppContext(school);
        var tenant = new FixedTenantProvider(school);
        return kind switch
        {
            "Classroom" => (await new GetDeletedClassroomsQueryHandler(ctx, tenant).Handle(new GetDeletedClassroomsQuery(), default)).Select(d => d.Id).ToList(),
            "Room" => (await new GetDeletedRoomsQueryHandler(ctx, tenant).Handle(new GetDeletedRoomsQuery(), default)).Select(d => d.Id).ToList(),
            "FeeCategory" => (await new GetDeletedFeeCategoriesQueryHandler(ctx, tenant).Handle(new GetDeletedFeeCategoriesQuery(), default)).Select(d => d.Id).ToList(),
            "Mention" => (await new GetDeletedMentionsQueryHandler(ctx, tenant).Handle(new GetDeletedMentionsQuery(), default)).Select(d => d.Id).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async Task<bool> IsDeletedAsync(string kind, Guid id)
    {
        await using var owner = _db.NewOwnerContext();
        return kind switch
        {
            "Classroom" => (await owner.Classrooms.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).IsDeleted,
            "Room" => (await owner.Rooms.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).IsDeleted,
            "FeeCategory" => (await owner.FeeCategories.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).IsDeleted,
            "Mention" => (await owner.Mentions.IgnoreQueryFilters().SingleAsync(x => x.Id == id)).IsDeleted,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Deleted_Entity_Is_Listed_Then_Restored_And_Leaves_The_Trash(string kind)
    {
        (await ListDeletedIdsAsync(kind, EcoleA)).Should().Contain(Tombstones[kind]);

        await RestoreAsync(kind, EcoleA, Tombstones[kind]);

        (await IsDeletedAsync(kind, Tombstones[kind])).Should().BeFalse();
        (await ListDeletedIdsAsync(kind, EcoleA)).Should().NotContain(Tombstones[kind]);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Restoring_When_An_Active_Row_Took_The_Identity_Returns_Active_Conflict_Without_Mutation(string kind)
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Add(MakeActive(kind, EcoleA));
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = async () => await RestoreAsync(kind, EcoleA, Tombstones[kind]);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
        (await IsDeletedAsync(kind, Tombstones[kind])).Should().BeTrue("la ligne supprimée reste inchangée");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Another_Tenant_Can_Neither_List_Nor_Restore_The_Tombstone(string kind)
    {
        (await ListDeletedIdsAsync(kind, EcoleB)).Should().NotContain(Tombstones[kind]);

        var act = async () => await RestoreAsync(kind, EcoleB, Tombstones[kind]);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await IsDeletedAsync(kind, Tombstones[kind])).Should().BeTrue();
    }

    [Fact]
    public async Task Creating_A_Room_FeeCategory_Or_Mention_That_Only_Exists_Deleted_Returns_Archived_Conflict()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var tenant = new FixedTenantProvider(EcoleA);

        Func<Task> room = async () => await new CreateRoomCommandHandler(ctx, tenant)
            .Handle(new CreateRoomCommand { Name = "Salle 1", Capacity = 30, BuildingId = BatimentActif }, default);
        Func<Task> category = async () => await new CreateFeeCategoryCommandHandler(ctx, tenant)
            .Handle(new CreateFeeCategoryCommand { Name = "Cantine" }, default);
        Func<Task> mention = async () => await new CreateMentionCommandHandler(ctx, tenant)
            .Handle(new CreateMentionCommand("Bien", 14), default);

        foreach (var act in new[] { room, category, mention })
        {
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code
                .Should().Be(SoftDeleteLifecycle.ArchivedEntityExists);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.Rooms.IgnoreQueryFilters().CountAsync(r => r.Name == "Salle 1")).Should().Be(1);
        (await owner.FeeCategories.IgnoreQueryFilters().CountAsync(c => c.Name == "Cantine")).Should().Be(1);
        (await owner.Mentions.IgnoreQueryFilters().CountAsync(m => m.Label == "Bien")).Should().Be(1);
    }

    [Fact]
    public async Task Creating_A_Classroom_Identity_That_Only_Exists_Deleted_Is_Refused_By_The_Shared_Guard()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var act = async () => await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
            ctx.Classrooms, EcoleA, c => c.Name == "6e A", "Une classe « 6e A »", default);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code
            .Should().Be(SoftDeleteLifecycle.ArchivedEntityExists);
    }

    [Fact]
    public async Task Restoring_A_Room_Whose_Building_Is_Deleted_Is_Refused()
    {
        var roomInDeletedBuilding = Guid.Parse("5c000000-0000-0000-0000-0000000000ff");
        await using (var owner = _db.NewOwnerContext())
        {
            var room = new Room { Id = roomInDeletedBuilding, SchoolId = EcoleA, BuildingId = BatimentSupprime, Name = "Orpheline", Capacity = 10 };
            owner.Rooms.Add(room);
            await owner.SaveChangesAsync(CancellationToken.None);
            room.SoftDelete("test");
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = async () => await RestoreAsync("Room", EcoleA, roomInDeletedBuilding);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PARENT_ENTITY_ARCHIVED");
        (await IsDeletedAsync("Room", roomInDeletedBuilding)).Should().BeTrue();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
