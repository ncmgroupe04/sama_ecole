using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using SamaEcole.Application.Buildings.Commands.DeleteBuilding;
using SamaEcole.Application.Buildings.Commands.RestoreBuilding;
using SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;
using SamaEcole.Application.Buildings.Queries.GetDeletedBuildings;
using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Classrooms.Commands.DeleteClassroom;
using SamaEcole.Application.Classrooms.Commands.RestoreClassroom;
using SamaEcole.Application.Classrooms.Queries.GetClassrooms;
using SamaEcole.Application.Classrooms.Queries.GetDeletedClassrooms;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.DeleteFeeCategory;
using SamaEcole.Application.Finance.Commands.RestoreFeeCategory;
using SamaEcole.Application.Finance.Queries.GetDeletedFeeCategories;
using SamaEcole.Application.Finance.Queries.GetFeeCategories;
using SamaEcole.Application.Grades.Commands.CreateMention;
using SamaEcole.Application.Grades.Commands.DeleteMention;
using SamaEcole.Application.Grades.Commands.RestoreMention;
using SamaEcole.Application.Grades.Queries.GetDeletedMentions;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Application.Rooms.Commands.CreateRoom;
using SamaEcole.Application.Rooms.Commands.DeleteRoom;
using SamaEcole.Application.Rooms.Commands.RestoreRoom;
using SamaEcole.Application.Rooms.Queries.GetDeletedRooms;
using SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Tâche 6 (plan soft delete 2026-10-01) — le cycle complet, de BOUT EN BOUT, pour chaque agrégat
/// restaurable du §0.1 de l'inventaire : création → visible normalement → suppression (via le VRAI
/// Handler de commande, pas un tombstone posé directement en base) → invisible des listes/sélecteurs
/// normaux ET présent en corbeille → restauration (vrai Handler) → visible normalement, hors corbeille.
///
/// Les suites existantes (<c>ReferenceEntitiesRestoreTests</c>, <c>BuildingsRestoreTests</c>,
/// <c>SchoolYearRestoreTests</c>) posent déjà leurs tombstones directement par EF puis vérifient la
/// restauration ; aucune n'enchaînait Create → Delete → Restore via les Handlers réels pour prouver que
/// la chaîne complète tient, de la création jusqu'à la disparition de la corbeille. C'est l'écart que
/// cette classe referme, plus les scénarios bout-en-bout non encore couverts : concurrence RÉELLE à la
/// création (deux Handlers qui ont chacun passé le pré-contrôle avant que l'un des deux n'écrive) et
/// partage d'identité entre deux tenants pour Classroom/Room (déjà prouvé ailleurs pour
/// Building/FeeCategory/Mention, voir SoftDeleteUniqueIndexTests).
/// </summary>
[Trait("Category", "MultiTenant")]
public class RestorableAggregatesEndToEndTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("81111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("82222222-2222-2222-2222-222222222222");
    private static readonly Guid BatimentA = Guid.Parse("8b000000-0000-0000-0000-00000000000a");
    private static readonly Guid BatimentB = Guid.Parse("8b000000-0000-0000-0000-00000000000b");
    private static readonly Guid Acteur = Guid.Parse("8f000000-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.Buildings.AddRange(
            new Building { Id = BatimentA, SchoolId = EcoleA, Name = "Bâtiment principal" },
            new Building { Id = BatimentB, SchoolId = EcoleB, Name = "Bâtiment principal" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static TestCurrentUser Director => new(Acteur, Role.Directeur);

    // ------------------------------------------------------------------ Cycle complet (Create → Delete → Restore)

    [Fact]
    public async Task Building_Full_Cycle_Create_Then_Invisible_Then_Trash_Then_Restore_Then_Visible_Again()
    {
        Guid id;
        uint rowVersion;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var result = await new CreateBuildingCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new CreateBuildingCommand { Name = "Bloc Administratif" }, default);
            id = result.Id;
            rowVersion = result.RowVersion;
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetBuildingsWithRoomsQueryHandler(ctx).Handle(new GetBuildingsWithRoomsQuery(), default);
            active.Should().Contain(b => b.Id == id, "visible normalement juste après création");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteBuildingCommandHandler(ctx, Director)
                .Handle(new DeleteBuildingCommand(id, rowVersion), default);
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetBuildingsWithRoomsQueryHandler(ctx).Handle(new GetBuildingsWithRoomsQuery(), default);
            active.Should().NotContain(b => b.Id == id, "invisible de la liste normale après suppression");

            var trash = await new GetDeletedBuildingsQueryHandler(ctx, Tenant(EcoleA)).Handle(new GetDeletedBuildingsQuery(), default);
            trash.Should().Contain(b => b.Id == id, "présent en corbeille après suppression");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreBuildingCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new RestoreBuildingCommand(id), default);
        }

        await using var final = _db.NewAppContext(EcoleA);
        var activeAfter = await new GetBuildingsWithRoomsQueryHandler(final).Handle(new GetBuildingsWithRoomsQuery(), default);
        activeAfter.Should().Contain(b => b.Id == id, "visible normalement après restauration");

        var trashAfter = await new GetDeletedBuildingsQueryHandler(final, Tenant(EcoleA)).Handle(new GetDeletedBuildingsQuery(), default);
        trashAfter.Should().NotContain(b => b.Id == id, "quitte la corbeille après restauration");
    }

    [Fact]
    public async Task Classroom_Full_Cycle_Create_Then_Invisible_Then_Trash_Then_Restore_Then_Visible_Again()
    {
        Guid id;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var result = await new CreateClassroomCommandHandler(ctx, Tenant(EcoleA), new NoOpKpiCache(), Injector(ctx))
                .Handle(new CreateClassroomCommand { Name = "CP Bout-en-bout", Level = "CP", Capacity = 35 }, default);
            id = result.Id;
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetClassroomsQueryHandler(ctx).Handle(new GetClassroomsQuery(), default);
            active.Should().Contain(c => c.Id == id, "visible normalement juste après création");
        }

        var rowVersion = await ClassroomXminAsync(EcoleA, id);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteClassroomCommandHandler(ctx, Director, new NoOpKpiCache())
                .Handle(new DeleteClassroomCommand(id, rowVersion), default);
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetClassroomsQueryHandler(ctx).Handle(new GetClassroomsQuery(), default);
            active.Should().NotContain(c => c.Id == id, "invisible du sélecteur de classes après suppression");

            var trash = await new GetDeletedClassroomsQueryHandler(ctx, Tenant(EcoleA)).Handle(new GetDeletedClassroomsQuery(), default);
            trash.Should().Contain(c => c.Id == id, "présente en corbeille après suppression");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreClassroomCommandHandler(ctx, Tenant(EcoleA), new NoOpKpiCache())
                .Handle(new RestoreClassroomCommand(id), default);
        }

        await using var final = _db.NewAppContext(EcoleA);
        var activeAfter = await new GetClassroomsQueryHandler(final).Handle(new GetClassroomsQuery(), default);
        activeAfter.Should().Contain(c => c.Id == id, "visible normalement après restauration");

        var trashAfter = await new GetDeletedClassroomsQueryHandler(final, Tenant(EcoleA)).Handle(new GetDeletedClassroomsQuery(), default);
        trashAfter.Should().NotContain(c => c.Id == id, "quitte la corbeille après restauration");
    }

    [Fact]
    public async Task Room_Full_Cycle_Create_Then_Invisible_Then_Trash_Then_Restore_Then_Visible_Again()
    {
        Guid id;
        uint rowVersion;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var result = await new CreateRoomCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new CreateRoomCommand { Name = "Salle Bout-en-bout", Capacity = 25, BuildingId = BatimentA }, default);
            id = result.Id;
            rowVersion = result.RowVersion;
        }

        async Task<bool> RoomVisibleAsync()
        {
            await using var ctx = _db.NewAppContext(EcoleA);
            var buildings = await new GetBuildingsWithRoomsQueryHandler(ctx).Handle(new GetBuildingsWithRoomsQuery(), default);
            return buildings.Any(b => b.Id == BatimentA && b.Rooms.Any(r => r.Id == id));
        }

        (await RoomVisibleAsync()).Should().BeTrue("visible normalement juste après création");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteRoomCommandHandler(ctx, Director)
                .Handle(new DeleteRoomCommand(id, rowVersion), default);
        }

        (await RoomVisibleAsync()).Should().BeFalse("invisible du bâtiment après suppression");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var trash = await new GetDeletedRoomsQueryHandler(ctx, Tenant(EcoleA)).Handle(new GetDeletedRoomsQuery(), default);
            trash.Should().Contain(r => r.Id == id, "présente en corbeille après suppression");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreRoomCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new RestoreRoomCommand(id), default);
        }

        (await RoomVisibleAsync()).Should().BeTrue("visible normalement après restauration");

        await using var final = _db.NewAppContext(EcoleA);
        var trashAfter = await new GetDeletedRoomsQueryHandler(final, Tenant(EcoleA)).Handle(new GetDeletedRoomsQuery(), default);
        trashAfter.Should().NotContain(r => r.Id == id, "quitte la corbeille après restauration");
    }

    [Fact]
    public async Task FeeCategory_Full_Cycle_Create_Then_Invisible_Then_Trash_Then_Restore_Then_Visible_Again()
    {
        Guid id;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var result = await new CreateFeeCategoryCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new CreateFeeCategoryCommand { Name = "Frais bout-en-bout" }, default);
            id = result.Id;
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetFeeCategoriesQueryHandler(ctx).Handle(new GetFeeCategoriesQuery(), default);
            active.Should().Contain(c => c.Id == id, "visible normalement juste après création");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteFeeCategoryCommandHandler(ctx, Director)
                .Handle(new DeleteFeeCategoryCommand(id), default);
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetFeeCategoriesQueryHandler(ctx).Handle(new GetFeeCategoriesQuery(), default);
            active.Should().NotContain(c => c.Id == id, "invisible de la liste des catégories après suppression");

            var trash = await new GetDeletedFeeCategoriesQueryHandler(ctx, Tenant(EcoleA)).Handle(new GetDeletedFeeCategoriesQuery(), default);
            trash.Should().Contain(c => c.Id == id, "présente en corbeille après suppression");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreFeeCategoryCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new RestoreFeeCategoryCommand(id), default);
        }

        await using var final = _db.NewAppContext(EcoleA);
        var activeAfter = await new GetFeeCategoriesQueryHandler(final).Handle(new GetFeeCategoriesQuery(), default);
        activeAfter.Should().Contain(c => c.Id == id, "visible normalement après restauration");

        var trashAfter = await new GetDeletedFeeCategoriesQueryHandler(final, Tenant(EcoleA)).Handle(new GetDeletedFeeCategoriesQuery(), default);
        trashAfter.Should().NotContain(c => c.Id == id, "quitte la corbeille après restauration");
    }

    [Fact]
    public async Task Mention_Full_Cycle_Create_Then_Invisible_Then_Trash_Then_Restore_Then_Visible_Again()
    {
        Guid? id;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var result = await new CreateMentionCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new CreateMentionCommand("Mention bout-en-bout", 12), default);
            id = result.Id;
        }
        id.Should().NotBeNull("une mention créée porte toujours un identifiant");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var active = await new GetMentionsQueryHandler(ctx).Handle(new GetMentionsQuery(), default);
            active.Should().Contain(m => m.Id == id, "visible normalement juste après création");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteMentionCommandHandler(ctx, Director)
                .Handle(new DeleteMentionCommand(id!.Value), default);
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            // Si plus aucune mention vivante n'existe dans le tenant, GetMentionsQueryHandler retombe
            // sur des seuils par défaut (Id = null) : on vérifie donc l'ABSENCE de l'id précis, pas
            // une liste vide — les deux comportements sont corrects et ne doivent pas être confondus.
            var active = await new GetMentionsQueryHandler(ctx).Handle(new GetMentionsQuery(), default);
            active.Should().NotContain(m => m.Id == id, "invisible de la liste des mentions après suppression");

            var trash = await new GetDeletedMentionsQueryHandler(ctx, Tenant(EcoleA)).Handle(new GetDeletedMentionsQuery(), default);
            trash.Should().Contain(m => m.Id == id, "présente en corbeille après suppression");
        }

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new RestoreMentionCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new RestoreMentionCommand(id.Value), default);
        }

        await using var final = _db.NewAppContext(EcoleA);
        var activeAfter = await new GetMentionsQueryHandler(final).Handle(new GetMentionsQuery(), default);
        activeAfter.Should().Contain(m => m.Id == id, "visible normalement après restauration");

        var trashAfter = await new GetDeletedMentionsQueryHandler(final, Tenant(EcoleA)).Handle(new GetDeletedMentionsQuery(), default);
        trashAfter.Should().NotContain(m => m.Id == id, "quitte la corbeille après restauration");
    }

    // ------------------------------------------------------------------ Deux créations, une même identité active

    /// <summary>
    /// Deux Handlers, chacun sur son propre DbContext (comme deux requêtes HTTP distinctes). Le
    /// pré-contrôle (<c>EnsureNoArchivedIdentityAsync</c>) ne porte que sur les identités ARCHIVÉES :
    /// il ne voit donc aucun doublon ici, et c'est bien l'index unique PARTIEL sur les lignes vivantes,
    /// en base, qui doit arbitrer — qu'il s'agisse d'une vraie course (deux connexions qui écrivent au
    /// même instant, comme <c>TeacherAssignmentLifecycleTests.Two_Simultaneous_Assignments_…</c>) ou,
    /// comme ici, de deux écritures qui se suivent sans qu'aucun pré-contrôle applicatif n'intervienne
    /// entre les deux. Non encore couvert pour Classroom/Room/SchoolYear (SoftDeleteUniqueIndexTests ne
    /// teste que Building/FeeCategory/Mention, et au niveau du DbContext, pas des Handlers).
    /// </summary>
    [Fact]
    public async Task Two_Concurrent_Classroom_Creations_With_The_Same_Name_Leave_Exactly_One_Row()
    {
        await using var ctxA = _db.NewAppContext(EcoleA);
        await using var ctxB = _db.NewAppContext(EcoleA);

        var command = new CreateClassroomCommand { Name = "6e Concurrente", Level = "6e", Capacity = 40 };

        await new CreateClassroomCommandHandler(ctxA, Tenant(EcoleA), new NoOpKpiCache(), Injector(ctxA))
            .Handle(command, default);

        var act = async () => await new CreateClassroomCommandHandler(ctxB, Tenant(EcoleA), new NoOpKpiCache(), Injector(ctxB))
            .Handle(command, default);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "la seconde création simultanée ne doit jamais aboutir à deux classes de même nom");

        await using var owner = _db.NewOwnerContext();
        (await owner.Classrooms.IgnoreQueryFilters().CountAsync(c => c.SchoolId == EcoleA && c.Name == "6e Concurrente"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Two_Concurrent_Room_Creations_With_The_Same_Name_Leave_Exactly_One_Row()
    {
        await using var ctxA = _db.NewAppContext(EcoleA);
        await using var ctxB = _db.NewAppContext(EcoleA);

        var command = new CreateRoomCommand { Name = "Salle Concurrente", Capacity = 20, BuildingId = BatimentA };

        await new CreateRoomCommandHandler(ctxA, Tenant(EcoleA)).Handle(command, default);

        var act = async () => await new CreateRoomCommandHandler(ctxB, Tenant(EcoleA)).Handle(command, default);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "la seconde création simultanée ne doit jamais aboutir à deux salles de même nom dans le même bâtiment");

        await using var owner = _db.NewOwnerContext();
        (await owner.Rooms.IgnoreQueryFilters().CountAsync(r => r.SchoolId == EcoleA && r.Name == "Salle Concurrente"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Two_Concurrent_School_Year_Creations_With_The_Same_Label_Leave_Exactly_One_Row()
    {
        await using var ctxA = _db.NewAppContext(EcoleA);
        await using var ctxB = _db.NewAppContext(EcoleA);

        // Même libellé, mais des périodes qui NE SE CHEVAUCHENT PAS : le garde-fou de chevauchement
        // (CreateSchoolYearCommandHandler, avant l'index unique) ne doit pas être celui qui arbitre ici
        // — c'est bien l'identité « libellé » qui est testée, pas la détection de chevauchement
        // (couverte par ailleurs). Seul l'index unique partiel (SchoolId, Label) peut encore refuser.
        var commandA = new CreateSchoolYearCommand
        {
            Label = "2033-2034", StartDate = new DateOnly(2033, 10, 1), EndDate = new DateOnly(2034, 6, 30)
        };
        var commandB = new CreateSchoolYearCommand
        {
            Label = "2033-2034", StartDate = new DateOnly(2043, 10, 1), EndDate = new DateOnly(2044, 6, 30)
        };

        await new CreateSchoolYearCommandHandler(ctxA, Tenant(EcoleA), TimeProvider.System).Handle(commandA, default);

        var act = async () => await new CreateSchoolYearCommandHandler(ctxB, Tenant(EcoleA), TimeProvider.System)
            .Handle(commandB, default);

        await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "la seconde création ne doit jamais aboutir à deux années de même libellé");

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.SchoolId == EcoleA && y.Label == "2033-2034"))
            .Should().Be(1);
    }

    // ------------------------------------------------------------------ Partage d'identité entre tenants

    /// <summary>
    /// L'unicité de Classroom/Room est posée par tenant (SchoolId dans la clé de l'index) : deux écoles
    /// doivent pouvoir chacune avoir une classe « 6e A » active, sans collision ni fuite. Déjà prouvé
    /// pour Building/FeeCategory/Mention (SoftDeleteUniqueIndexTests.Two_Tenants_Can_Use_The_Same_Identity) ;
    /// Classroom et Room ne l'étaient pas encore.
    /// </summary>
    [Fact]
    public async Task Two_Tenants_Can_Each_Have_An_Active_Classroom_With_The_Same_Name()
    {
        var command = new CreateClassroomCommand { Name = "6e A Partagée", Level = "6e", Capacity = 40 };

        await using (var ctxA = _db.NewAppContext(EcoleA))
        {
            await new CreateClassroomCommandHandler(ctxA, Tenant(EcoleA), new NoOpKpiCache(), Injector(ctxA)).Handle(command, default);
        }

        var act = async () =>
        {
            await using var ctxB = _db.NewAppContext(EcoleB);
            await new CreateClassroomCommandHandler(ctxB, Tenant(EcoleB), new NoOpKpiCache(), Injector(ctxB)).Handle(command, default);
        };

        await act.Should().NotThrowAsync("l'unicité du nom de classe est par école, pas globale");
    }

    [Fact]
    public async Task Two_Tenants_Can_Each_Have_An_Active_Room_With_The_Same_Name()
    {
        await using (var ctxA = _db.NewAppContext(EcoleA))
        {
            await new CreateRoomCommandHandler(ctxA, Tenant(EcoleA))
                .Handle(new CreateRoomCommand { Name = "Salle Partagée", Capacity = 20, BuildingId = BatimentA }, default);
        }

        var act = async () =>
        {
            await using var ctxB = _db.NewAppContext(EcoleB);
            await new CreateRoomCommandHandler(ctxB, Tenant(EcoleB))
                .Handle(new CreateRoomCommand { Name = "Salle Partagée", Capacity = 20, BuildingId = BatimentB }, default);
        };

        await act.Should().NotThrowAsync("l'unicité du nom de salle est par bâtiment/école, pas globale");
    }

    // ------------------------------------------------------------------ Bloquer le parent (inventaire §6)

    /// <summary>
    /// Inventaire §6 : « Salle/bâtiment : désactiver seulement si aucun usage historique courant ».
    /// DeleteBuildingCommandHandler porte une garde explicite — un bâtiment encore lié à des salles ne
    /// peut pas être archivé — mais aucune suite ne l'exerçait : ni succès ni refus n'étaient prouvés.
    /// </summary>
    [Fact]
    public async Task Deleting_A_Building_That_Still_Has_A_Room_Is_Blocked_And_Changes_Nothing()
    {
        Guid buildingId;
        uint rowVersion;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var building = await new CreateBuildingCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new CreateBuildingCommand { Name = "Bâtiment Avec Salle" }, default);
            buildingId = building.Id;
            rowVersion = building.RowVersion;

            await new CreateRoomCommandHandler(ctx, Tenant(EcoleA))
                .Handle(new CreateRoomCommand { Name = "Salle Occupée", Capacity = 20, BuildingId = buildingId }, default);
        }

        await using var ctxDelete = _db.NewAppContext(EcoleA);
        var act = async () => await new DeleteBuildingCommandHandler(ctxDelete, Director)
            .Handle(new DeleteBuildingCommand(buildingId, rowVersion), default);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("salles rattachées");

        await using var owner = _db.NewOwnerContext();
        var row = await owner.Buildings.IgnoreQueryFilters().SingleAsync(b => b.Id == buildingId);
        row.IsDeleted.Should().BeFalse("le refus ne doit laisser aucune mutation partielle sur le bâtiment");
        (await owner.Rooms.IgnoreQueryFilters().CountAsync(r => r.BuildingId == buildingId && !r.IsDeleted))
            .Should().Be(1, "la salle reste intacte : la garde refuse AVANT toute écriture");
    }

    // ------------------------------------------------------------------ Isolation tenant sur la corbeille

    /// <summary>
    /// Une école ne doit jamais pouvoir déduire, lister ni restaurer la ligne supprimée d'une autre —
    /// même quand les deux partagent la même identité métier (même nom). Couvre le second volet du
    /// point 2 de la checklist (le premier, le partage d'identité ACTIVE, est couvert ci-dessus) : ici,
    /// c'est l'identité SUPPRIMÉE d'une école que l'autre ne doit pas voir apparaître.
    /// </summary>
    [Fact]
    public async Task A_Tenant_Cannot_See_Or_Restore_Another_Tenants_Deleted_Classroom_Sharing_The_Same_Name()
    {
        Guid idA;
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var created = await new CreateClassroomCommandHandler(ctx, Tenant(EcoleA), new NoOpKpiCache(), Injector(ctx))
                .Handle(new CreateClassroomCommand { Name = "Nom Partagé Supprimé", Level = "6e", Capacity = 40 }, default);
            idA = created.Id;
        }
        var rowVersion = await ClassroomXminAsync(EcoleA, idA);
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteClassroomCommandHandler(ctx, Director, new NoOpKpiCache())
                .Handle(new DeleteClassroomCommand(idA, rowVersion), default);
        }

        // École B crée puis supprime SA PROPRE classe de même nom : deux tombstones distincts, chacun
        // sous son SchoolId.
        await using (var ctx = _db.NewAppContext(EcoleB))
        {
            var createdB = await new CreateClassroomCommandHandler(ctx, Tenant(EcoleB), new NoOpKpiCache(), Injector(ctx))
                .Handle(new CreateClassroomCommand { Name = "Nom Partagé Supprimé", Level = "6e", Capacity = 40 }, default);
            var rowVersionB = await ClassroomXminAsync(EcoleB, createdB.Id);
            await new DeleteClassroomCommandHandler(ctx, Director, new NoOpKpiCache())
                .Handle(new DeleteClassroomCommand(createdB.Id, rowVersionB), default);
        }

        // École B ne doit voir QUE sa propre classe supprimée en corbeille, jamais celle d'École A.
        await using var ctxTrashB = _db.NewAppContext(EcoleB);
        var trashB = await new GetDeletedClassroomsQueryHandler(ctxTrashB, Tenant(EcoleB)).Handle(new GetDeletedClassroomsQuery(), default);
        trashB.Should().NotContain(c => c.Id == idA, "la corbeille d'une école ne montre jamais la ligne supprimée d'une autre");

        // Et ne peut pas restaurer l'identifiant d'École A, même en le connaissant (404, jamais 200 ni
        // 409 qui révélerait l'existence de la ligne chez le voisin).
        var act = async () => await new RestoreClassroomCommandHandler(ctxTrashB, Tenant(EcoleB), new NoOpKpiCache())
            .Handle(new RestoreClassroomCommand(idA), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();

        await using var owner = _db.NewOwnerContext();
        (await owner.Classrooms.IgnoreQueryFilters().SingleAsync(c => c.Id == idA)).IsDeleted.Should().BeTrue(
            "la tentative de restauration depuis l'autre tenant n'a rien changé à la ligne d'École A");
    }

    private async Task<uint> ClassroomXminAsync(Guid school, Guid id)
    {
        await using var ctx = _db.NewAppContext(school);
        return await ctx.Classrooms.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync();
    }

    private static ClassSubjectTemplateInjector Injector(IApplicationDbContext db) =>
        new(db, new NationalSeriesTemplateProvider(), Director);

    private static ITenantProvider Tenant(Guid schoolId) => new FixedTenantProvider(schoolId);

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class NoOpKpiCache : IKpiCacheService
    {
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
            factory(cancellationToken);

        public void Invalidate(string key) { }
    }
}
