using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 1 — les trois tables (uniform_items, uniform_variants, student_uniforms) tiennent-elles leurs
/// garanties DANS LA BASE, et pas seulement dans le C# ? Sur une vraie base PostgreSQL, avec le rôle applicatif bridé :
///
///   1. la RLS FILTRE réellement (RlsCoverageTests prouve seulement qu'elle existe) ;
///   2. le rôle applicatif ne peut physiquement rien supprimer (règle #6) mais peut modifier ;
///   3. les FK composites (SchoolId, X) interdisent à une ligne de désigner celle d'une autre école — une FK
///      simple s'exécute hors RLS et laisserait passer ;
///   4. les CHECK refusent une remise sans mouvement de stock, une remise non facturée sans motif ;
///   5. les index uniques PARTIELS : une ligne active par élève et par modèle, et une ligne archivée libère sa place ;
///   6. les deux fonctions de purge emportent les nouvelles tables sans échouer en 23503.
/// </summary>
[Trait("Category", "MultiTenant")]
public class UniformsPersistenceTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid AnneeA1 = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001"); // inactive : celle qu'on purge
    private static readonly Guid AnneeA2 = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002"); // active : le témoin
    private static readonly Guid AnneeB1 = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");

    private static readonly Guid UserId = Guid.Parse("ffffffff-0000-0000-0000-0000000000f1");

    private Graph _a1 = null!;
    private Graph _a2 = null!;
    private Graph _b1 = null!;

    /// <summary>Les identifiants d'un exercice complet : de la ligne de frais jusqu'à la dotation remise.</summary>
    private sealed record Graph(
        Guid SchoolId, Guid YearId, Guid EnrollmentId, Guid StudentId, Guid FeeCategoryId, Guid FeeLineId,
        Guid InventoryItemId, Guid MovementId, Guid ItemId, Guid VariantId, Guid UniformId);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        _a1 = Seed(owner, EcoleA, AnneeA1, "A1", 2025, isActive: false);
        _a2 = Seed(owner, EcoleA, AnneeA2, "A2", 2026, isActive: true);
        _b1 = Seed(owner, EcoleB, AnneeB1, "B1", 2026, isActive: true);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    /// <summary>Un exercice complet, dont une tenue facturée et REMISE (mouvement de stock compris).</summary>
    private static Graph Seed(ApplicationDbContext owner, Guid schoolId, Guid yearId, string tag, int year, bool isActive)
    {
        var classroomId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();
        var feeCategoryId = Guid.NewGuid();
        var feeLineId = Guid.NewGuid();
        var inventoryCategoryId = Guid.NewGuid();
        var inventoryItemId = Guid.NewGuid();
        var movementId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var variantId = Guid.NewGuid();
        var uniformId = Guid.NewGuid();

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = yearId, SchoolId = schoolId, Label = $"{year}-{year + 1}",
            StartDate = new DateOnly(year, 10, 1), EndDate = new DateOnly(year + 1, 7, 31), IsActive = isActive
        });

        owner.Classrooms.Add(new Classroom
        {
            Id = classroomId, SchoolId = schoolId, Name = $"CM2-{tag}", Level = "Primaire", Capacity = 40
        });

        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = schoolId, Matricule = $"ELEV-{tag}-0001", FullName = $"Élève {tag}",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
        });

        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = schoolId, StudentId = studentId, SchoolYearId = yearId,
            ClassroomId = classroomId, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed,
            TotalDue = 170_000m, AmountPaid = 50_000m, ReceiptNumber = $"REC-{tag}-0001",
            EnrolledAt = DateTimeOffset.UtcNow
        });

        owner.FeeCategories.Add(new FeeCategory { Id = feeCategoryId, SchoolId = schoolId, Name = $"Uniforme {tag}", IsOptional = true });

        owner.EnrollmentFeeLines.Add(new EnrollmentFeeLine
        {
            Id = feeLineId, SchoolId = schoolId, EnrollmentId = enrollmentId, FeeCategoryId = feeCategoryId,
            Designation = "Uniforme", UnitAmount = 25_000m, Months = 1, LineTotal = 25_000m
        });

        owner.InventoryCategories.Add(new InventoryCategory { Id = inventoryCategoryId, SchoolId = schoolId, Name = $"Tenues {tag}" });

        owner.InventoryItems.Add(new InventoryItem
        {
            Id = inventoryItemId, SchoolId = schoolId, Name = $"Polo bleu — 10 ans {tag}", CategoryId = inventoryCategoryId,
            QuantityTotal = 9, QuantityAvailable = 9, IsConsumable = true
        });

        owner.StockMovements.Add(new StockMovement
        {
            Id = movementId, SchoolId = schoolId, ItemId = inventoryItemId, Type = StockMovementType.Sortie, Quantity = 1,
            MovementDate = new DateOnly(year, 10, 5), Reason = "Remise de tenue", QuantityTotalAfter = 9, QuantityAvailableAfter = 9
        });

        owner.UniformItems.Add(new UniformItem
        {
            Id = itemId, SchoolId = schoolId, Name = $"Polo {tag}", Kind = UniformKind.Uniforme, Gender = UniformGender.Mixte,
            FeeCategoryId = feeCategoryId, QuantityIncluded = 1
        });

        owner.UniformVariants.Add(new UniformVariant
        {
            Id = variantId, SchoolId = schoolId, UniformItemId = itemId, SizeLabel = "10 ans", SortOrder = 1,
            InventoryItemId = inventoryItemId
        });

        owner.StudentUniforms.Add(new StudentUniform
        {
            Id = uniformId, SchoolId = schoolId, StudentId = studentId, EnrollmentId = enrollmentId, SchoolYearId = yearId,
            UniformItemId = itemId, UniformVariantId = variantId, Quantity = 1, Status = StudentUniformStatus.Delivered,
            DeliveredOn = new DateOnly(year, 10, 5), DeliveredByUserId = UserId, DeliveryStockMovementId = movementId,
            BillingStatus = UniformBillingStatus.Billed, FeeLineId = feeLineId,
            ItemLabel = "Polo bleu", SizeLabel = "10 ans", StudentLabel = $"Élève {tag}"
        });

        return new Graph(schoolId, yearId, enrollmentId, studentId, feeCategoryId, feeLineId,
            inventoryItemId, movementId, itemId, variantId, uniformId);
    }

    /// <summary>Une dotation VALIDE, encore « demandée », sans l'avoir ajoutée au contexte.</summary>
    private static StudentUniform NewRequested(Graph g, Guid? itemId = null) => new()
    {
        SchoolId = g.SchoolId, StudentId = g.StudentId, EnrollmentId = g.EnrollmentId, SchoolYearId = g.YearId,
        UniformItemId = itemId ?? g.ItemId, Quantity = 1, Status = StudentUniformStatus.Requested
    };

    private async Task<string?> SqlStateOfAsync(Func<ApplicationDbContext, Task> write)
    {
        await using var owner = _db.NewOwnerContext();

        try
        {
            await write(owner);
            await owner.SaveChangesAsync(CancellationToken.None);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)
        {
            return pg.SqlState;
        }
        catch (DuplicateRecordException)
        {
            // ApplicationDbContext traduit déjà une violation d'unicité (23505) en 409 : c'est ce que verra l'API.
            return PostgresErrorCodes.UniqueViolation;
        }
    }

    // ------------------------------------------------------------------ 1. RLS

    [Theory]
    [InlineData("uniform_items")]
    [InlineData("uniform_variants")]
    [InlineData("student_uniforms")]
    public async Task RawSqlQuery_Should_Never_Return_Other_Schools_Row(string table)
    {
        // Sans EF ni filtre C# : si une ligne de l'École B remonte ici, la policy RLS ne filtre pas réellement.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" WHERE "SchoolId" = @ecoleB;""";
        command.Parameters.AddWithValue("ecoleB", EcoleB);

        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);

        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" WHERE "SchoolId" = @ecoleA;""";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("ecoleA", EcoleA);

        ((long)(await command.ExecuteScalarAsync())!).Should().BeGreaterThan(0, "l'école courante voit bien ses propres lignes");
    }

    [Theory]
    [InlineData("uniform_items")]
    [InlineData("uniform_variants")]
    [InlineData("student_uniforms")]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All(string table)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}";""";

        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }

    [Fact]
    public async Task Writing_A_Row_For_Another_School_Is_Refused_By_The_Policy()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        // WITH CHECK : sous le tenant A, insérer une ligne portant le SchoolId de B est refusé par la base.
        command.CommandText =
            """
            INSERT INTO "uniform_items" ("Id","SchoolId","Name","Kind","Gender","QuantityIncluded","CreatedAt","IsDeleted")
            VALUES (@id, @ecoleB, 'Intrus', 'Uniforme', 'Mixte', 1, now(), false);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("ecoleB", EcoleB);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    // ------------------------------------------------------------------ 2. Droits : jamais de DELETE

    [Theory]
    [InlineData("uniform_items")]
    [InlineData("uniform_variants")]
    [InlineData("student_uniforms")]
    public async Task The_Application_Role_Can_Never_Delete(string table)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""DELETE FROM "{table}" WHERE "SchoolId" = @ecoleA;""";
        command.Parameters.AddWithValue("ecoleA", EcoleA);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Theory]
    [InlineData("uniform_items", "Notes")]
    [InlineData("student_uniforms", "ReturnReason")]
    public async Task The_Application_Role_Can_Still_Update(string table, string column)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""UPDATE "{table}" SET "{column}" = 'x' WHERE "SchoolId" = @ecoleA;""";
        command.Parameters.AddWithValue("ecoleA", EcoleA);

        (await command.ExecuteNonQueryAsync()).Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------ 3. FK composites tenant-safe

    [Fact]
    public async Task A_Line_Cannot_Point_To_Another_Schools_Enrollment()
    {
        var state = await SqlStateOfAsync(owner =>
        {
            var line = NewRequested(_a2, itemId: _a2.ItemId);
            line.EnrollmentId = _b1.EnrollmentId; // l'inscription d'une AUTRE école
            owner.StudentUniforms.Add(line);
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.ForeignKeyViolation,
            "une FK simple vers enrollments.Id laisserait passer : seule la FK composite (SchoolId, Id) protège");
    }

    [Fact]
    public async Task A_Size_Cannot_Point_To_Another_Schools_Inventory_Lot()
    {
        var state = await SqlStateOfAsync(owner =>
        {
            owner.UniformVariants.Add(new UniformVariant
            {
                SchoolId = EcoleA, UniformItemId = _a2.ItemId, SizeLabel = "12 ans", SortOrder = 2,
                InventoryItemId = _b1.InventoryItemId // le lot d'une AUTRE école
            });
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task An_Item_Cannot_Point_To_Another_Schools_Fee_Category()
    {
        var state = await SqlStateOfAsync(owner =>
        {
            owner.UniformItems.Add(new UniformItem
            {
                SchoolId = EcoleA, Name = "Jupe", FeeCategoryId = _b1.FeeCategoryId // le frais d'une AUTRE école
            });
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task A_Line_Cannot_Cite_Another_Schools_Stock_Movement()
    {
        var state = await SqlStateOfAsync(async owner =>
        {
            var line = await owner.StudentUniforms.IgnoreQueryFilters().SingleAsync(u => u.Id == _a2.UniformId);
            line.DeliveryStockMovementId = _b1.MovementId;
        });

        state.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    // ------------------------------------------------------------------ 4. CHECK

    [Fact]
    public async Task A_Delivered_Line_Without_A_Stock_Movement_Is_Refused()
    {
        var state = await SqlStateOfAsync(owner =>
        {
            var line = NewRequested(_a2);
            line.UniformItemId = _a2.ItemId;
            line.Status = StudentUniformStatus.Delivered; // ni date, ni auteur, ni mouvement
            owner.StudentUniforms.Add(line);
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.CheckViolation,
            "sans mouvement, le journal de stock ne recollerait plus avec les compteurs");
    }

    [Fact]
    public async Task A_Delivered_Not_Billed_Line_Without_A_Reason_Is_Refused()
    {
        var state = await SqlStateOfAsync(async owner =>
        {
            var line = await owner.StudentUniforms.IgnoreQueryFilters().SingleAsync(u => u.Id == _a2.UniformId);
            line.BillingStatus = UniformBillingStatus.NotBilled;
            line.FeeLineId = null;
            line.NotBilledReason = null;
        });

        state.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Delivered_Not_Billed_Line_With_A_Reason_Is_Accepted()
    {
        var state = await SqlStateOfAsync(async owner =>
        {
            var line = await owner.StudentUniforms.IgnoreQueryFilters().SingleAsync(u => u.Id == _a2.UniformId);
            line.BillingStatus = UniformBillingStatus.NotBilled;
            line.FeeLineId = null;
            line.NotBilledReason = "Dotation de la mairie";
        });

        state.Should().BeNull();
    }

    [Fact]
    public async Task A_Billed_Line_Without_Its_Fee_Line_Is_Refused()
    {
        var state = await SqlStateOfAsync(async owner =>
        {
            var line = await owner.StudentUniforms.IgnoreQueryFilters().SingleAsync(u => u.Id == _a2.UniformId);
            line.FeeLineId = null; // Billed sans preuve de facturation
        });

        state.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task A_Returned_Line_Without_Its_Return_Movement_Is_Refused()
    {
        var state = await SqlStateOfAsync(async owner =>
        {
            var line = await owner.StudentUniforms.IgnoreQueryFilters().SingleAsync(u => u.Id == _a2.UniformId);
            line.Status = StudentUniformStatus.Returned;
            line.ReturnReason = "Mauvaise taille";
        });

        state.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_Non_Positive_Quantity_Is_Refused(int quantity)
    {
        var state = await SqlStateOfAsync(owner =>
        {
            var line = NewRequested(_a2);
            line.UniformItemId = _a2.ItemId;
            line.Status = StudentUniformStatus.Cancelled; // hors de l'unicité active : seule la quantité est en cause
            line.Quantity = quantity;
            owner.StudentUniforms.Add(line);
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task An_Item_Must_Cover_At_Least_One_Unit()
    {
        var state = await SqlStateOfAsync(owner =>
        {
            owner.UniformItems.Add(new UniformItem { SchoolId = EcoleA, Name = "Casquette", QuantityIncluded = 0 });
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    // ------------------------------------------------------------------ 5. Index uniques partiels

    [Fact]
    public async Task Only_One_Active_Line_Per_Enrollment_And_Item()
    {
        // _a2 porte déjà une ligne Delivered pour ce modèle : une seconde ligne « demandée » est un doublon actif.
        var state = await SqlStateOfAsync(owner =>
        {
            owner.StudentUniforms.Add(NewRequested(_a2));
            return Task.CompletedTask;
        });

        state.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_Returned_Line_Frees_The_Place_For_A_New_Request()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var line = await owner.StudentUniforms.IgnoreQueryFilters().SingleAsync(u => u.Id == _a2.UniformId);
            // Une ligne retournée n'est plus « active » (Requested/Delivered) : l'index partiel la laisse de côté.
            line.Status = StudentUniformStatus.Returned;
            line.ReturnStockMovementId = _a2.MovementId;
            line.ReturnReason = "Échange";
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var state = await SqlStateOfAsync(owner =>
        {
            owner.StudentUniforms.Add(NewRequested(_a2));
            return Task.CompletedTask;
        });

        state.Should().BeNull("un échange = retour puis nouvelle ligne : l'ancienne ne bloque plus");
    }

    [Fact]
    public async Task Two_Active_Items_Cannot_Share_Name_And_Gender_But_An_Archived_One_Frees_The_Name()
    {
        (await SqlStateOfAsync(owner =>
        {
            owner.UniformItems.Add(new UniformItem { SchoolId = EcoleA, Name = "Polo A2", Gender = UniformGender.Mixte });
            return Task.CompletedTask;
        })).Should().Be(PostgresErrorCodes.UniqueViolation);

        // Même nom, autre public : autorisé.
        (await SqlStateOfAsync(owner =>
        {
            owner.UniformItems.Add(new UniformItem { SchoolId = EcoleA, Name = "Polo A2", Gender = UniformGender.Fille });
            return Task.CompletedTask;
        })).Should().BeNull();

        // Archiver le modèle libère son nom (index partiel sur les lignes actives).
        (await SqlStateOfAsync(async owner =>
        {
            (await owner.UniformItems.IgnoreQueryFilters().SingleAsync(i => i.Id == _a2.ItemId)).SoftDelete("test");
        })).Should().BeNull();

        (await SqlStateOfAsync(owner =>
        {
            owner.UniformItems.Add(new UniformItem { SchoolId = EcoleA, Name = "Polo A2", Gender = UniformGender.Mixte });
            return Task.CompletedTask;
        })).Should().BeNull();
    }

    [Fact]
    public async Task A_Size_Label_And_An_Inventory_Lot_Belong_To_One_Active_Size_Only()
    {
        (await SqlStateOfAsync(owner =>
        {
            owner.UniformVariants.Add(new UniformVariant
            {
                SchoolId = EcoleA, UniformItemId = _a2.ItemId, SizeLabel = "10 ans", InventoryItemId = Guid.NewGuid()
            });
            return Task.CompletedTask;
        })).Should().NotBeNull("le libellé est déjà pris pour ce modèle — et le lot inexistant ferait de toute façon échouer la FK");

        var duplicateLot = await SqlStateOfAsync(owner =>
        {
            owner.UniformVariants.Add(new UniformVariant
            {
                SchoolId = EcoleA, UniformItemId = _a2.ItemId, SizeLabel = "12 ans",
                InventoryItemId = _a2.InventoryItemId // déjà porté par « 10 ans »
            });
            return Task.CompletedTask;
        });

        duplicateLot.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    // ------------------------------------------------------------------ 6. Purges

    [Fact]
    public async Task Deleting_A_School_Year_Purges_Its_Uniform_Lines_And_Only_Those()
    {
        await using (var app = _db.NewAppContext(EcoleA))
        {
            await _db.NewSchoolYearPurgeService(app).PurgeAsync(EcoleA, AnneeA1, CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();

        (await owner.StudentUniforms.IgnoreQueryFilters().CountAsync(u => u.SchoolYearId == AnneeA1)).Should().Be(0);
        (await owner.StudentUniforms.IgnoreQueryFilters().CountAsync(u => u.SchoolYearId == AnneeA2)).Should().Be(1,
            "l'autre exercice de la même école est intact");
        (await owner.StudentUniforms.IgnoreQueryFilters().CountAsync(u => u.SchoolYearId == AnneeB1)).Should().Be(1,
            "l'autre école est intacte");

        // Modèles et tailles ne sont pas rattachés à une année : ils restent.
        (await owner.UniformItems.IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA)).Should().Be(2);
        // Le journal de stock n'est jamais touché par la suppression d'une année.
        (await owner.StockMovements.IgnoreQueryFilters().CountAsync(m => m.Id == _a1.MovementId)).Should().Be(1);
    }

    [Fact]
    public async Task Resetting_A_School_Purges_The_Three_Uniform_Tables_Without_An_Fk_Error()
    {
        await using (var app = _db.NewAppContext(EcoleA))
        {
            await _db.NewResetSchoolDataService(app).ResetAsync(EcoleA, CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();

        (await owner.StudentUniforms.IgnoreQueryFilters().CountAsync(u => u.SchoolId == EcoleA)).Should().Be(0);
        (await owner.UniformVariants.IgnoreQueryFilters().CountAsync(v => v.SchoolId == EcoleA)).Should().Be(0);
        (await owner.UniformItems.IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA)).Should().Be(0);

        (await owner.StudentUniforms.IgnoreQueryFilters().CountAsync(u => u.SchoolId == EcoleB)).Should().Be(1,
            "la remise à neuf s'arrête à la frontière du tenant");
        (await owner.UniformItems.IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleB)).Should().Be(1);
    }
}
