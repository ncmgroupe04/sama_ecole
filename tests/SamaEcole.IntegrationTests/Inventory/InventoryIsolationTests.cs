using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Inventory.Commands.CreateInventoryItem;
using SamaEcole.Application.Inventory.Commands.CreateItemAssignment;
using SamaEcole.Application.Inventory.Queries.GetInventoryItems;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Inventory;

/// <summary>
/// Module Inventaire — les quatre nouvelles tables tenant (inventory_categories, inventory_items,
/// stock_movements, item_assignments) tiennent-elles l'isolation, à la fois côté C# (Handlers, filtre
/// EF) ET côté base (policy RLS, AGENTS.md règle #2) ? Même démarche que
/// <see cref="Buildings.BuildingsIsolationTests"/> : <see cref="Multitenancy.RlsCoverageTests"/>
/// prouve seulement que la policy EXISTE, ce fichier prouve qu'elle FILTRE réellement.
///
/// Ce fichier prouve en plus une propriété qui n'appartient qu'à ce module : le journal de stock est
/// append-only AU NIVEAU DE LA BASE, et pas seulement par convention de code.
/// </summary>
[Trait("Category", "MultiTenant")]
public class InventoryIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");

    private static readonly Guid CategorieA = Guid.Parse("cccccccc-1111-0000-0000-00000000000a");
    private static readonly Guid CategorieB = Guid.Parse("cccccccc-1111-0000-0000-00000000000b");
    private static readonly Guid BienA = Guid.Parse("dddddddd-1111-0000-0000-00000000000a");
    private static readonly Guid BienB = Guid.Parse("dddddddd-1111-0000-0000-00000000000b");
    private static readonly Guid MouvementA = Guid.Parse("11111111-2222-0000-0000-00000000000a");
    private static readonly Guid MouvementB = Guid.Parse("11111111-2222-0000-0000-00000000000b");
    private static readonly Guid PretA = Guid.Parse("22222222-3333-0000-0000-00000000000a");
    private static readonly Guid PretB = Guid.Parse("22222222-3333-0000-0000-00000000000b");

    private static readonly DateOnly Aujourdhui = new(2026, 9, 15);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 45 });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Modou Diop", BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB });

        owner.InventoryCategories.AddRange(
            new InventoryCategory { Id = CategorieA, SchoolId = EcoleA, Name = "Manuels scolaires" },
            new InventoryCategory { Id = CategorieB, SchoolId = EcoleB, Name = "Informatique" });

        owner.InventoryItems.AddRange(
            new InventoryItem
            {
                Id = BienA, SchoolId = EcoleA, Name = "Manuel de mathématiques CM2", CategoryId = CategorieA,
                QuantityTotal = 40, QuantityAvailable = 39, Condition = ItemCondition.Bon
            },
            new InventoryItem
            {
                Id = BienB, SchoolId = EcoleB, Name = "Vidéoprojecteur Epson", CategoryId = CategorieB,
                QuantityTotal = 2, QuantityAvailable = 2, Condition = ItemCondition.Neuf
            });

        owner.StockMovements.AddRange(
            new StockMovement
            {
                Id = MouvementA, SchoolId = EcoleA, ItemId = BienA, Type = StockMovementType.Entree, Quantity = 40,
                MovementDate = Aujourdhui, Reason = "Dotation IEF 2026", QuantityTotalAfter = 40, QuantityAvailableAfter = 40
            },
            new StockMovement
            {
                Id = MouvementB, SchoolId = EcoleB, ItemId = BienB, Type = StockMovementType.Entree, Quantity = 2,
                MovementDate = Aujourdhui, Reason = "Achat", QuantityTotalAfter = 2, QuantityAvailableAfter = 2
            });

        owner.ItemAssignments.AddRange(
            new ItemAssignment
            {
                Id = PretA, SchoolId = EcoleA, ItemId = BienA, Quantity = 1,
                BeneficiaryType = AssignmentBeneficiaryType.Eleve, StudentId = EleveA,
                BeneficiaryLabel = "Awa Fall (ELEV-A-0001)", AssignedOn = Aujourdhui, Status = AssignmentStatus.EnCours
            },
            new ItemAssignment
            {
                Id = PretB, SchoolId = EcoleB, ItemId = BienB, Quantity = 1,
                BeneficiaryType = AssignmentBeneficiaryType.Eleve, StudentId = EleveB,
                BeneficiaryLabel = "Modou Diop (ELEV-B-0001)", AssignedOn = Aujourdhui, Status = AssignmentStatus.EnCours
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static TimeProvider Clock() => new FixedClock(Aujourdhui);

    // ------------------------------------------------------------------ Isolation côté Handlers

    [Fact]
    public async Task Creating_An_Item_In_Another_Schools_Category_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateInventoryItemCommandHandler(ctx, new FixedTenantProvider(EcoleA), Clock());

        var command = new CreateInventoryItemCommand
        {
            Name = "Bien intrusif",
            CategoryId = CategorieB,
            InitialQuantity = 5
        };

        var act = async () => await handler.Handle(command, CancellationToken.None);

        // Même principe que CreateRoomCommandHandler vis-à-vis du bâtiment : une catégorie d'une autre
        // école est structurellement introuvable (Global Query Filter + RLS), traduit en
        // ValidationException (422) sur le champ précis plutôt qu'en 500 issu de la contrainte FK.
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Assigning_An_Item_To_Another_Schools_Student_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateItemAssignmentCommandHandler(ctx, new FixedTenantProvider(EcoleA), Clock());

        var command = new CreateItemAssignmentCommand
        {
            ItemId = BienA,
            Quantity = 1,
            BeneficiaryType = AssignmentBeneficiaryType.Eleve,
            BeneficiaryId = EleveB,
            RowVersion = await RowVersionOfItemAsync(ctx, BienA)
        };

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>(
            "un élève d'une autre école ne doit jamais pouvoir figurer sur une décharge de l'école A");
    }

    [Fact]
    public async Task Items_List_Never_Leaks_Another_Schools_Item()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetInventoryItemsQueryHandler(ctx);

        var result = await handler.Handle(new GetInventoryItemsQuery(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Name.Should().Be("Manuel de mathématiques CM2");
        result.Items.Should().NotContain(i => i.Id == BienB, "le bien de l'École B ne doit jamais apparaître");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Creating_An_Item_Never_Accepts_A_Client_Supplied_SchoolId()
    {
        // SchoolId vient toujours de ITenantProvider, jamais du client (AGENTS.md règle #10) —
        // CreateInventoryItemCommand n'a d'ailleurs aucune propriété SchoolId à falsifier.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateInventoryItemCommandHandler(ctx, new FixedTenantProvider(EcoleA), Clock());

        var result = await handler.Handle(
            new CreateInventoryItemCommand { Name = "Craie blanche", CategoryId = CategorieA, InitialQuantity = 100 },
            CancellationToken.None);

        var created = await ctx.InventoryItems.FindAsync(result.Id);
        created!.SchoolId.Should().Be(EcoleA);

        // Le mouvement d'entrée né avec le lot doit porter le MÊME tenant : une ligne de journal
        // orpheline dans une autre école serait invisible à son propriétaire et lisible ailleurs.
        var movement = ctx.StockMovements.Single(m => m.ItemId == result.Id);
        movement.SchoolId.Should().Be(EcoleA);
    }

    // ------------------------------------------------------------------ Preuve RLS au niveau BASE (SQL brut, sans EF)

    [Theory]
    [InlineData("inventory_categories")]
    [InlineData("inventory_items")]
    [InlineData("stock_movements")]
    [InlineData("item_assignments")]
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

    [Theory]
    [InlineData("inventory_categories")]
    [InlineData("inventory_items")]
    [InlineData("stock_movements")]
    [InlineData("item_assignments")]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All(string table)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}";""";

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "sans tenant positionné, la policy ne doit laisser passer AUCUNE ligne");
    }

    // ------------------------------------------------------------------ Journal append-only, imposé par la BASE

    [Fact]
    public async Task Stock_Movements_Journal_Refuses_Update_For_The_Application_Role()
    {
        // Le caractère append-only du journal ne repose PAS sur la discipline des Handlers : la
        // migration n'accorde que SELECT et INSERT au rôle applicatif. Si ce GRANT venait à être
        // élargi, ce test tomberait — et une école pourrait maquiller son inventaire avant un contrôle.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE "stock_movements" SET "Reason" = 'rature' WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", MouvementA);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Stock_Movements_Journal_Refuses_Delete_For_The_Application_Role()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """DELETE FROM "stock_movements" WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", MouvementA);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Catalog_Tables_Still_Accept_Update_For_The_Application_Role()
    {
        // Contrepoint indispensable au test précédent : sans lui, un GRANT trop restrictif posé par
        // erreur sur les trois autres tables passerait inaperçu — le soft delete et le verrou
        // optimiste ont besoin de l'UPDATE.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """UPDATE "inventory_items" SET "LocationLabel" = 'Réserve A' WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", BienA);

        var rows = await command.ExecuteNonQueryAsync();

        rows.Should().Be(1);
    }

    private static async Task<uint> RowVersionOfItemAsync(
        SamaEcole.Persistence.ApplicationDbContext ctx, Guid itemId)
    {
        var item = await ctx.InventoryItems.FindAsync(itemId);
        return ctx.Entry(item!).Property<uint>("xmin").CurrentValue;
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Horloge figée : les Handlers du module datent les mouvements et les prêts « aujourd'hui » et
/// refusent une date future. Avec TimeProvider.System, ces tests dépendraient du jour de leur
/// exécution — un jeu de données daté de la rentrée deviendrait invalide à la relecture en janvier.
/// </summary>
file sealed class FixedClock(DateOnly today) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() =>
        new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
