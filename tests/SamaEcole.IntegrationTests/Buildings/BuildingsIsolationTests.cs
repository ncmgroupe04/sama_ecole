using FluentAssertions;
using SamaEcole.Application.Buildings.Commands.CreateBuilding;
using SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Rooms.Commands.CreateRoom;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Buildings;

/// <summary>
/// Module Infrastructures (Bâtiments/Salles) — les deux nouvelles tables tenant (buildings, rooms)
/// tiennent-elles l'isolation, à la fois côté C# (Handlers, filtre EF) ET côté base (policy RLS,
/// AGENTS.md règle #2) ? Même démarche que <see cref="Documents.DocumentsModuleIsolationTests"/> :
/// <see cref="Multitenancy.RlsCoverageTests"/> prouve seulement que la policy EXISTE, ce fichier prouve
/// qu'elle FILTRE réellement.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BuildingsIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid BatimentA = Guid.Parse("aaaaaaaa-1111-0000-0000-00000000000a");
    private static readonly Guid BatimentB = Guid.Parse("aaaaaaaa-1111-0000-0000-00000000000b");
    private static readonly Guid SalleA = Guid.Parse("bbbbbbbb-1111-0000-0000-00000000000a");
    private static readonly Guid SalleB = Guid.Parse("bbbbbbbb-1111-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Buildings.AddRange(
            new Building { Id = BatimentA, SchoolId = EcoleA, Name = "Bâtiment A" },
            new Building { Id = BatimentB, SchoolId = EcoleB, Name = "Bâtiment B" });

        owner.Rooms.AddRange(
            new Room { Id = SalleA, SchoolId = EcoleA, Name = "Salle A1", Capacity = 30, Type = RoomType.SalleDeClasse, BuildingId = BatimentA },
            new Room { Id = SalleB, SchoolId = EcoleB, Name = "Salle B1", Capacity = 25, Type = RoomType.SalleDeClasse, BuildingId = BatimentB });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Creating_A_Room_For_Another_Schools_Building_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateRoomCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateRoomCommand { Name = "Salle Intrusive", Capacity = 20, Type = RoomType.SalleDeClasse, BuildingId = BatimentB };
        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Même principe que CreateGradeCommandHandler vis-à-vis de l'élève/la matière : un bâtiment
        // d'une autre école est structurellement introuvable (Global Query Filter + RLS), traduit en
        // ValidationException (422) sur le champ précis plutôt qu'en 500 issu de la contrainte FK.
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Buildings_List_Never_Leaks_Another_Schools_Building_Or_Room()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetBuildingsWithRoomsQueryHandler(ctx);

        var result = await handler.Handle(new GetBuildingsWithRoomsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.Name.Should().Be("Bâtiment A");
        result.Should().NotContain(b => b.Id == BatimentB, "le bâtiment de l'École B ne doit jamais apparaître");
        result.Single().Rooms.Should().ContainSingle().Which.Name.Should().Be("Salle A1");
    }

    [Fact]
    public async Task Creating_A_Building_Never_Accepts_A_Client_Supplied_SchoolId()
    {
        // SchoolId vient toujours de ITenantProvider, jamais du client (AGENTS.md règle #10) —
        // CreateBuildingCommand n'a d'ailleurs aucune propriété SchoolId à falsifier.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateBuildingCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var result = await handler.Handle(new CreateBuildingCommand { Name = "Nouveau Bâtiment" }, CancellationToken.None);

        var owned = await ctx.Buildings.FindAsync(result.Id);
        owned!.SchoolId.Should().Be(EcoleA);
    }

    // ------------------------------------------------------------------ Preuve RLS au niveau BASE (SQL brut, sans EF)

    [Theory]
    [InlineData("buildings")]
    [InlineData("rooms")]
    public async Task RawSqlQuery_Should_Never_Return_Other_Schools_Row(string table)
    {
        // Sans EF ni filtre C# : si une ligne de l'École B remonte ici, c'est que la policy RLS ne
        // filtre pas réellement (RlsCoverageTests prouve seulement qu'elle EXISTE, pas qu'elle filtre).
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" WHERE "SchoolId" = @ecoleB;""";
        command.Parameters.AddWithValue("ecoleB", EcoleB);

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, $"la RLS doit masquer toute ligne de {table} appartenant à l'École B sous le tenant A");
    }

    [Fact]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "buildings";""";

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "sans tenant, current_setting('app.current_school_id') est vide : aucune ligne ne doit être visible");
    }
}

/// <summary>Fournit un SchoolId fixe, sans passer par le contexte HTTP — suffisant pour un handler appelé directement en test.</summary>
file sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
