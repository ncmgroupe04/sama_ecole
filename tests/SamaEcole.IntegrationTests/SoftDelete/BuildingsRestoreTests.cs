using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using SamaEcole.Application.Buildings.Commands.RestoreBuilding;
using SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;
using SamaEcole.Application.Buildings.Queries.GetDeletedBuildings;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Conception soft delete 2026-10-01 §3.2 — parcours de référence (Bâtiments) : création bloquée par
/// <c>ARCHIVED_ENTITY_EXISTS</c> quand seule une ligne supprimée porte l'identité, restauration volontaire,
/// <c>ACTIVE_ENTITY_CONFLICT</c> quand une ligne active a repris l'identité, et isolation tenant de la corbeille.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BuildingsRestoreTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("41111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("42222222-2222-2222-2222-222222222222");
    private static readonly Guid BatimentSupprimeA = Guid.Parse("4aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid BatimentSupprimeB = Guid.Parse("4bbbbbbb-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        var a = new Building { Id = BatimentSupprimeA, SchoolId = EcoleA, Name = "Bloc A" };
        var b = new Building { Id = BatimentSupprimeB, SchoolId = EcoleB, Name = "Bloc A" };
        owner.Buildings.AddRange(a, b);
        await owner.SaveChangesAsync(CancellationToken.None);
        a.SoftDelete("test");
        b.SoftDelete("test");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<int> CountRowsAsync(Guid school, string name)
    {
        await using var owner = _db.NewOwnerContext();
        return await owner.Buildings.IgnoreQueryFilters().CountAsync(x => x.SchoolId == school && x.Name == name);
    }

    [Fact]
    public async Task Creating_An_Identity_That_Only_Exists_Deleted_Returns_Archived_Conflict_And_Inserts_Nothing()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateBuildingCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var act = async () => await handler.Handle(new CreateBuildingCommand { Name = "Bloc A" }, CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ARCHIVED_ENTITY_EXISTS");
        (await CountRowsAsync(EcoleA, "Bloc A")).Should().Be(1, "aucune nouvelle ligne ne doit être créée");
    }

    [Fact]
    public async Task Restoring_A_Deleted_Building_Makes_It_Visible_Again()
    {
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var deleted = await new GetDeletedBuildingsQueryHandler(ctx, new FixedTenantProvider(EcoleA))
                .Handle(new GetDeletedBuildingsQuery(), CancellationToken.None);
            deleted.Should().ContainSingle(d => d.Id == BatimentSupprimeA);
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreBuildingCommandHandler(ctx, new FixedTenantProvider(EcoleA))
                .Handle(new RestoreBuildingCommand(BatimentSupprimeA), CancellationToken.None);
        }

        await using var read = _db.NewAppContext(EcoleA);
        var active = await new GetBuildingsWithRoomsQueryHandler(read)
            .Handle(new GetBuildingsWithRoomsQuery(), CancellationToken.None);
        active.Should().ContainSingle(b => b.Id == BatimentSupprimeA);

        await using var owner = _db.NewOwnerContext();
        var row = await owner.Buildings.IgnoreQueryFilters().SingleAsync(b => b.Id == BatimentSupprimeA);
        row.IsDeleted.Should().BeFalse();
        row.DeletedAt.Should().BeNull();
        row.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task Restoring_When_An_Active_Building_Took_The_Name_Returns_Active_Conflict_Without_Mutation()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Buildings.Add(new Building { SchoolId = EcoleA, Name = "Bloc A" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = async () => await new RestoreBuildingCommandHandler(ctx, new FixedTenantProvider(EcoleA))
            .Handle(new RestoreBuildingCommand(BatimentSupprimeA), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ACTIVE_ENTITY_CONFLICT");

        await using var owner2 = _db.NewOwnerContext();
        (await owner2.Buildings.IgnoreQueryFilters().SingleAsync(b => b.Id == BatimentSupprimeA))
            .IsDeleted.Should().BeTrue("la ligne supprimée reste inchangée");
        (await CountRowsAsync(EcoleA, "Bloc A")).Should().Be(2);
    }

    [Fact]
    public async Task Another_Tenant_Can_Neither_List_Nor_Restore_The_Deleted_Building()
    {
        await using (var ctx = _db.NewAppContext(EcoleB))
        {
            var deleted = await new GetDeletedBuildingsQueryHandler(ctx, new FixedTenantProvider(EcoleB))
                .Handle(new GetDeletedBuildingsQuery(), CancellationToken.None);
            deleted.Should().NotContain(d => d.Id == BatimentSupprimeA);
        }

        await using var attacker = _db.NewAppContext(EcoleB);
        var act = async () => await new RestoreBuildingCommandHandler(attacker, new FixedTenantProvider(EcoleB))
            .Handle(new RestoreBuildingCommand(BatimentSupprimeA), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();

        await using var owner = _db.NewOwnerContext();
        (await owner.Buildings.IgnoreQueryFilters().SingleAsync(b => b.Id == BatimentSupprimeA))
            .IsDeleted.Should().BeTrue();
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
