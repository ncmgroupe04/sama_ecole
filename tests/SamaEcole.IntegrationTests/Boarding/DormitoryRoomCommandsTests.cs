using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Rooms.CreateDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.DeleteDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.RestoreDormitoryRoom;
using SamaEcole.Application.Boarding.Rooms.UpdateDormitoryRoom;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Commandes de gestion des chambres (lot B, tâche 3) — contre un vrai PostgreSQL, RLS comprise.</summary>
[Trait("Category", "MultiTenant")]
public class DormitoryRoomCommandsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f3111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("f3222222-2222-2222-2222-222222222222");
    private static readonly Guid DirecteurA = Guid.Parse("f3111111-0000-0000-0000-0000000000d1");
    private static readonly Guid PavillonA1 = Guid.Parse("f3111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavillonA2 = Guid.Parse("f3111111-0000-0000-0000-00000000b002");
    private static readonly Guid PavillonB = Guid.Parse("f3222222-0000-0000-0000-00000000b001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Dormitories.AddRange(
            new Dormitory { Id = PavillonA1, SchoolId = EcoleA, Name = "Pavillon A1", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavillonA2, SchoolId = EcoleA, Name = "Pavillon A2", Gender = DormitoryGender.Filles },
            new Dormitory { Id = PavillonB, SchoolId = EcoleB, Name = "Pavillon B", Gender = DormitoryGender.Garcons });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<DormitoryRoomResult> CreateAsync(Guid school, Guid dormitory, string name, int beds = 3)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new CreateDormitoryRoomCommandHandler(ctx, new BoardingTenant(school)).Handle(
            new CreateDormitoryRoomCommand { DormitoryId = dormitory, Name = name, BedCount = beds }, CancellationToken.None);
    }

    private async Task DeleteAsync(Guid school, DormitoryRoomResult room)
    {
        await using var ctx = _db.NewAppContext(school);
        await new DeleteDormitoryRoomCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteDormitoryRoomCommand(room.Id, room.RowVersion), CancellationToken.None);
    }

    /// <summary>Supprime logiquement tous les lits d'une chambre (ce que fera la commande de lit de la tâche 4).</summary>
    private async Task SoftDeleteBedsAsync(Guid room)
    {
        await using var owner = _db.NewOwnerContext();
        foreach (var bed in await owner.Beds.IgnoreQueryFilters().Where(b => b.DormitoryRoomId == room).ToListAsync())
        {
            bed.SoftDelete("test");
        }

        await owner.SaveChangesAsync();
    }

    private async Task<int> BedCountAsync(Guid room)
    {
        await using var owner = _db.NewOwnerContext();
        return await owner.Beds.IgnoreQueryFilters().CountAsync(b => b.DormitoryRoomId == room);
    }

    [Fact]
    public async Task Creating_A_Room_Generates_Its_Beds_Numbered_From_1_All_Available()
    {
        var room = await CreateAsync(EcoleA, PavillonA1, "Chambre 101", beds: 4);

        room.Beds.Select(b => b.BedNumber).Should().Equal(1, 2, 3, 4);
        room.Beds.Should().OnlyContain(b => b.Status == BedStatus.Available && b.OccupantBoarderId == null);
        (await BedCountAsync(room.Id)).Should().Be(4);
    }

    [Fact]
    public async Task A_Dormitory_That_Does_Not_Exist_Or_Belongs_To_Another_School_Is_Refused()
    {
        var unknown = async () => await CreateAsync(EcoleA, Guid.NewGuid(), "Chambre 1");
        await unknown.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("DormitoryId"));

        var otherSchool = async () => await CreateAsync(EcoleA, PavillonB, "Chambre 1");
        await otherSchool.Should().ThrowAsync<ValidationException>().Where(e => e.Errors.ContainsKey("DormitoryId"));
    }

    [Fact]
    public async Task Creating_A_Name_That_Only_Exists_Deleted_Returns_ARCHIVED_ENTITY_EXISTS_And_Creates_No_Bed()
    {
        var first = await CreateAsync(EcoleA, PavillonA1, "Chambre 101", beds: 2);
        await SoftDeleteBedsAsync(first.Id);
        await DeleteAsync(EcoleA, first);

        var act = async () => await CreateAsync(EcoleA, PavillonA1, "Chambre 101", beds: 5);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ArchivedEntityExists);
        await using var owner = _db.NewOwnerContext();
        (await owner.Beds.IgnoreQueryFilters().CountAsync(b => b.SchoolId == EcoleA)).Should().Be(2, "aucun lit orphelin");
    }

    [Fact]
    public async Task The_Same_Room_Name_Can_Exist_In_Two_Dormitories()
    {
        await CreateAsync(EcoleA, PavillonA1, "Chambre 1");

        var act = async () => await CreateAsync(EcoleA, PavillonA2, "Chambre 1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Renaming_With_A_Stale_RowVersion_Is_Rejected()
    {
        var room = await CreateAsync(EcoleA, PavillonA1, "Chambre 1");
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var renamed = await new UpdateDormitoryRoomCommandHandler(ctx)
                .Handle(new UpdateDormitoryRoomCommand(room.Id, "Chambre 1 bis", room.RowVersion), CancellationToken.None);
            renamed.Name.Should().Be("Chambre 1 bis");
            renamed.Beds.Should().HaveCount(3, "le renommage ne touche pas aux lits");
        }

        await using var stale = _db.NewAppContext(EcoleA);
        var act = async () => await new UpdateDormitoryRoomCommandHandler(stale)
            .Handle(new UpdateDormitoryRoomCommand(room.Id, "Chambre 1 ter", room.RowVersion), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Deleting_A_Room_That_Still_Has_Live_Beds_Returns_RESOURCE_IN_USE()
    {
        var room = await CreateAsync(EcoleA, PavillonA1, "Chambre 1");

        var act = async () => await DeleteAsync(EcoleA, room);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ResourceInUse);
    }

    [Fact]
    public async Task A_Room_Without_Live_Beds_Can_Be_Deleted_And_Restored()
    {
        var room = await CreateAsync(EcoleA, PavillonA1, "Chambre 1");
        await SoftDeleteBedsAsync(room.Id);
        await DeleteAsync(EcoleA, room);

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            (await ctx.DormitoryRooms.AnyAsync(r => r.Id == room.Id)).Should().BeFalse();
            await new RestoreDormitoryRoomCommandHandler(ctx, new BoardingTenant(EcoleA))
                .Handle(new RestoreDormitoryRoomCommand(room.Id), CancellationToken.None);
        }

        await using var visible = _db.NewAppContext(EcoleA);
        (await visible.DormitoryRooms.AnyAsync(r => r.Id == room.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_Into_A_Deleted_Dormitory_Returns_PARENT_ENTITY_ARCHIVED()
    {
        var room = await CreateAsync(EcoleA, PavillonA2, "Chambre 1");
        await SoftDeleteBedsAsync(room.Id);
        await DeleteAsync(EcoleA, room);
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == PavillonA2)).SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new RestoreDormitoryRoomCommandHandler(ctx, new BoardingTenant(EcoleA))
            .Handle(new RestoreDormitoryRoomCommand(room.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PARENT_ENTITY_ARCHIVED");
    }

    [Fact]
    public async Task Restoring_Fails_With_ACTIVE_ENTITY_CONFLICT_When_The_Name_Was_Taken()
    {
        var room = await CreateAsync(EcoleA, PavillonA1, "Chambre 1");
        await SoftDeleteBedsAsync(room.Id);
        await DeleteAsync(EcoleA, room);
        await using (var owner = _db.NewOwnerContext())
        {
            owner.DormitoryRooms.Add(new DormitoryRoom { SchoolId = EcoleA, DormitoryId = PavillonA1, Name = "Chambre 1" });
            await owner.SaveChangesAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new RestoreDormitoryRoomCommandHandler(ctx, new BoardingTenant(EcoleA))
            .Handle(new RestoreDormitoryRoomCommand(room.Id), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
    }

    [Fact]
    public async Task A_Room_Of_Another_School_Is_Not_Found()
    {
        var other = await CreateAsync(EcoleB, PavillonB, "Chambre B");

        await using var ctx = _db.NewAppContext(EcoleA);
        var update = async () => await new UpdateDormitoryRoomCommandHandler(ctx)
            .Handle(new UpdateDormitoryRoomCommand(other.Id, "x", other.RowVersion), CancellationToken.None);
        await update.Should().ThrowAsync<KeyNotFoundException>();

        var delete = async () => await new DeleteDormitoryRoomCommandHandler(ctx, new TestCurrentUser(DirecteurA))
            .Handle(new DeleteDormitoryRoomCommand(other.Id, other.RowVersion), CancellationToken.None);
        await delete.Should().ThrowAsync<KeyNotFoundException>();
    }
}
