using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Commands.DeleteInventoryItem;
using SamaEcole.Application.Uniforms;
using SamaEcole.Application.Uniforms.Commands.CreateUniformItem;
using SamaEcole.Application.Uniforms.Commands.CreateUniformVariant;
using SamaEcole.Application.Uniforms.Commands.DeleteUniformItem;
using SamaEcole.Application.Uniforms.Commands.DeleteUniformVariant;
using SamaEcole.Application.Uniforms.Commands.UpdateUniformItem;
using SamaEcole.Application.Uniforms.Commands.UpdateUniformVariant;
using SamaEcole.Application.Uniforms.Queries.GetUniformItems;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Uniforms;

/// <summary>
/// Module Tenues, Phase 2 — le catalogue (modèles et tailles) sur PostgreSQL réel, sous le rôle applicatif bridé.
///
/// Ce que ces tests protègent et qu'un test unitaire ne peut pas couvrir : la création d'un lot d'inventaire dans la
/// MÊME transaction que la taille (un doublon ne doit laisser aucun lot orphelin), la portée tenant (la catégorie
/// « Tenues » d'une autre école n'est jamais réutilisée), le verrou optimiste xmin et l'archivage refusé tant qu'un
/// besoin en attente utilise le modèle.
/// </summary>
[Trait("Category", "MultiTenant")]
public class UniformCatalogTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid FraisA = Guid.Parse("f0000000-0000-0000-0000-00000000000a");
    private static readonly Guid FraisB = Guid.Parse("f0000000-0000-0000-0000-00000000000b");
    private static readonly Guid CategorieTenuesB = Guid.Parse("c0000000-0000-0000-0000-00000000000b");
    private static readonly Guid ModeleB = Guid.Parse("a0000000-0000-0000-0000-00000000000b");

    private static readonly Guid Acteur = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.FeeCategories.AddRange(
            new FeeCategory { Id = FraisA, SchoolId = EcoleA, Name = "Uniforme", IsOptional = true },
            new FeeCategory { Id = FraisB, SchoolId = EcoleB, Name = "Uniforme", IsOptional = true });

        // L'école B a déjà sa catégorie « Tenues » et un modèle : l'école A ne doit jamais s'en servir.
        owner.InventoryCategories.Add(new InventoryCategory { Id = CategorieTenuesB, SchoolId = EcoleB, Name = "Tenues" });
        owner.UniformItems.Add(new UniformItem { Id = ModeleB, SchoolId = EcoleB, Name = "Polo bleu", FeeCategoryId = FraisB });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ aides

    private async Task<UniformItemDto> CreateItemAsync(
        string name = "Polo bleu", Guid? feeCategoryId = null, UniformGender gender = UniformGender.Mixte,
        int quantityIncluded = 1)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await new CreateUniformItemCommandHandler(ctx, new FixedTenantProvider(EcoleA)).Handle(
            new CreateUniformItemCommand
            {
                Name = name, Kind = UniformKind.Uniforme, Gender = gender, FeeCategoryId = feeCategoryId,
                QuantityIncluded = quantityIncluded
            },
            CancellationToken.None);
    }

    private async Task<UniformVariantDto> CreateVariantAsync(Guid itemId, string size, int sortOrder = 0)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await new CreateUniformVariantCommandHandler(ctx, new FixedTenantProvider(EcoleA)).Handle(
            new CreateUniformVariantCommand { UniformItemId = itemId, SizeLabel = size, SortOrder = sortOrder },
            CancellationToken.None);
    }

    private async Task<IReadOnlyList<UniformItemDto>> ListAsync(Guid schoolId)
    {
        await using var ctx = _db.NewAppContext(schoolId);
        return await new GetUniformItemsQueryHandler(ctx).Handle(new GetUniformItemsQuery(), CancellationToken.None);
    }

    /// <summary>Un besoin EN ATTENTE sur ce modèle, posé par le propriétaire : prépare le cas « archivage refusé ».</summary>
    private async Task SeedPendingNeedAsync(Guid itemId, Guid? variantId)
    {
        await using var owner = _db.NewOwnerContext();

        var classroomId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var yearId = Guid.NewGuid();
        var enrollmentId = Guid.NewGuid();

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = yearId, SchoolId = EcoleA, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.Add(new Classroom { Id = classroomId, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Students.Add(new Student
        {
            Id = studentId, SchoolId = EcoleA, Matricule = "ELEV-A-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = classroomId
        });
        owner.Enrollments.Add(new Enrollment
        {
            Id = enrollmentId, SchoolId = EcoleA, StudentId = studentId, SchoolYearId = yearId, ClassroomId = classroomId,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 100_000m,
            ReceiptNumber = "REC-A-0001", EnrolledAt = DateTimeOffset.UtcNow
        });
        owner.StudentUniforms.Add(new StudentUniform
        {
            SchoolId = EcoleA, StudentId = studentId, EnrollmentId = enrollmentId, SchoolYearId = yearId,
            UniformItemId = itemId, UniformVariantId = variantId, Quantity = 1, Status = StudentUniformStatus.Requested
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------------ modèles

    [Fact]
    public async Task Creating_An_Item_Returns_It_With_Its_Fee_Category_And_A_Row_Version()
    {
        var item = await CreateItemAsync("  Polo bleu ", FraisA, UniformGender.Garcon, quantityIncluded: 2);

        item.Name.Should().Be("Polo bleu", "le nom est nettoyé");
        item.Gender.Should().Be("Garcon");
        item.Kind.Should().Be("Uniforme");
        item.FeeCategoryId.Should().Be(FraisA);
        item.FeeCategoryName.Should().Be("Uniforme");
        item.QuantityIncluded.Should().Be(2);
        item.RowVersion.Should().BeGreaterThan(0u);
        item.Variants.Should().BeEmpty();
    }

    [Fact]
    public async Task An_Item_Cannot_Use_Another_Schools_Fee_Category()
    {
        var act = () => CreateItemAsync("Jupe", feeCategoryId: FraisB);

        (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainKey("FeeCategoryId");
    }

    [Fact]
    public async Task A_Duplicate_Name_For_The_Same_Public_Is_A_Conflict_But_Another_Public_Is_Fine()
    {
        await CreateItemAsync("Polo bleu", gender: UniformGender.Mixte);

        var duplicate = () => CreateItemAsync("Polo bleu", gender: UniformGender.Mixte);
        await duplicate.Should().ThrowAsync<DuplicateRecordException>();

        var otherPublic = await CreateItemAsync("Polo bleu", gender: UniformGender.Fille);
        otherPublic.Gender.Should().Be("Fille");
    }

    [Fact]
    public async Task Updating_An_Item_With_A_Stale_Token_Is_Refused_And_Renames_Its_Lots()
    {
        var item = await CreateItemAsync("Polo bleu");
        await CreateVariantAsync(item.Id, "10 ans");

        await using (var first = _db.NewAppContext(EcoleA))
        {
            var updated = await new UpdateUniformItemCommandHandler(first).Handle(
                new UpdateUniformItemCommand
                {
                    Id = item.Id, Name = "Polo marine", Kind = UniformKind.Uniforme, Gender = UniformGender.Mixte,
                    QuantityIncluded = 1, RowVersion = item.RowVersion
                },
                CancellationToken.None);

            updated.Name.Should().Be("Polo marine");
            updated.RowVersion.Should().NotBe(item.RowVersion);
        }

        // Deuxième administrateur, même jeton d'AVANT : refus franc, jamais un écrasement silencieux (règle #5).
        await using (var second = _db.NewAppContext(EcoleA))
        {
            var act = () => new UpdateUniformItemCommandHandler(second).Handle(
                new UpdateUniformItemCommand
                {
                    Id = item.Id, Name = "Autre nom", Kind = UniformKind.Uniforme, Gender = UniformGender.Mixte,
                    QuantityIncluded = 1, RowVersion = item.RowVersion
                },
                CancellationToken.None);

            await act.Should().ThrowAsync<ConcurrencyConflictException>();
        }

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.UniformItems.SingleAsync(i => i.Id == item.Id)).Name.Should().Be("Polo marine");
        (await verify.InventoryItems.SingleAsync()).Name.Should().Be("Polo marine — 10 ans",
            "le lot de la taille suit le renommage du modèle, pour rester lisible dans l'écran Inventaire");
    }

    [Fact]
    public async Task An_Item_Of_Another_School_Is_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => new UpdateUniformItemCommandHandler(ctx).Handle(
            new UpdateUniformItemCommand
            {
                Id = ModeleB, Name = "Intrus", Kind = UniformKind.Uniforme, Gender = UniformGender.Mixte,
                QuantityIncluded = 1, RowVersion = 1
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ tailles et lots

    [Fact]
    public async Task Creating_A_Size_Creates_Its_Inventory_Lot_In_The_Schools_Own_Tenues_Category()
    {
        var item = await CreateItemAsync("Polo bleu");

        var variant = await CreateVariantAsync(item.Id, " 10 ans ", sortOrder: 2);

        variant.SizeLabel.Should().Be("10 ans");
        variant.QuantityTotal.Should().Be(0);
        variant.QuantityAvailable.Should().Be(0);

        await using var verify = _db.NewAppContext(EcoleA);
        var lot = await verify.InventoryItems.SingleAsync(i => i.Id == variant.InventoryItemId);

        lot.Name.Should().Be("Polo bleu — 10 ans");
        lot.IsConsumable.Should().BeTrue("une tenue se donne, elle ne se prête pas");
        lot.Condition.Should().Be(ItemCondition.Neuf);

        var category = await verify.InventoryCategories.SingleAsync(c => c.Id == lot.CategoryId);
        category.Name.Should().Be("Tenues");
        category.SchoolId.Should().Be(EcoleA, "la catégorie « Tenues » de l'école B n'est jamais réutilisée");
        category.Id.Should().NotBe(CategorieTenuesB);
    }

    [Fact]
    public async Task Several_Sizes_Share_One_Tenues_Category()
    {
        var item = await CreateItemAsync("Polo bleu");

        await CreateVariantAsync(item.Id, "8 ans", 1);
        await CreateVariantAsync(item.Id, "10 ans", 2);

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.InventoryCategories.CountAsync()).Should().Be(1);
        (await verify.InventoryItems.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_Duplicate_Size_Leaves_No_Orphan_Lot()
    {
        var item = await CreateItemAsync("Polo bleu");
        await CreateVariantAsync(item.Id, "10 ans");

        var act = () => CreateVariantAsync(item.Id, "10 ans");

        await act.Should().ThrowAsync<DuplicateRecordException>();

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.InventoryItems.CountAsync()).Should().Be(1,
            "le lot et la taille naissent dans la même transaction : un refus n'en laisse aucun");
        (await verify.UniformVariants.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_Size_Cannot_Be_Added_To_Another_Schools_Item()
    {
        var act = () => CreateVariantAsync(ModeleB, "10 ans");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Updating_A_Size_Renames_It_And_Its_Lot()
    {
        var item = await CreateItemAsync("Polo bleu");
        var variant = await CreateVariantAsync(item.Id, "10 ans");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            var updated = await new UpdateUniformVariantCommandHandler(ctx).Handle(
                new UpdateUniformVariantCommand { Id = variant.Id, SizeLabel = "10-11 ans", SortOrder = 5 },
                CancellationToken.None);

            updated.SizeLabel.Should().Be("10-11 ans");
            updated.SortOrder.Should().Be(5);
        }

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.InventoryItems.SingleAsync(i => i.Id == variant.InventoryItemId)).Name
            .Should().Be("Polo bleu — 10-11 ans");
    }

    // ------------------------------------------------------------------ archivage

    [Fact]
    public async Task A_Size_With_A_Pending_Need_Cannot_Be_Archived()
    {
        var item = await CreateItemAsync("Polo bleu");
        var variant = await CreateVariantAsync(item.Id, "10 ans");
        await SeedPendingNeedAsync(item.Id, variant.Id);

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => new DeleteUniformVariantCommandHandler(ctx, new TestCurrentUser(Acteur)).Handle(
            new DeleteUniformVariantCommand { Id = variant.Id }, CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_VARIANT_IN_USE");
    }

    [Fact]
    public async Task Archiving_A_Size_Frees_Its_Label_And_Keeps_Its_Stock_Lot()
    {
        var item = await CreateItemAsync("Polo bleu");
        var variant = await CreateVariantAsync(item.Id, "10 ans");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteUniformVariantCommandHandler(ctx, new TestCurrentUser(Acteur)).Handle(
                new DeleteUniformVariantCommand { Id = variant.Id }, CancellationToken.None);
        }

        (await ListAsync(EcoleA)).Single().Variants.Should().BeEmpty();

        await using var verify = _db.NewAppContext(EcoleA);
        (await verify.InventoryItems.CountAsync(i => i.Id == variant.InventoryItemId)).Should().Be(1,
            "le lot et son journal de stock restent : l'archivage d'une taille n'efface aucun stock");

        // Le libellé est libéré (index partiel) : la même taille peut être recréée.
        var again = await CreateVariantAsync(item.Id, "10 ans");
        again.Id.Should().NotBe(variant.Id);
    }

    [Fact]
    public async Task An_Item_With_A_Pending_Need_Cannot_Be_Archived()
    {
        var item = await CreateItemAsync("Polo bleu");
        await SeedPendingNeedAsync(item.Id, variantId: null);

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => new DeleteUniformItemCommandHandler(ctx, new TestCurrentUser(Acteur)).Handle(
            new DeleteUniformItemCommand { Id = item.Id, RowVersion = item.RowVersion }, CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_ITEM_IN_USE");
    }

    [Fact]
    public async Task Archiving_An_Item_Archives_Its_Sizes_In_The_Same_Transaction()
    {
        var item = await CreateItemAsync("Polo bleu");
        await CreateVariantAsync(item.Id, "8 ans");
        await CreateVariantAsync(item.Id, "10 ans");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new DeleteUniformItemCommandHandler(ctx, new TestCurrentUser(Acteur)).Handle(
                new DeleteUniformItemCommand { Id = item.Id, RowVersion = item.RowVersion }, CancellationToken.None);
        }

        (await ListAsync(EcoleA)).Should().BeEmpty();

        await using var owner = _db.NewOwnerContext();
        (await owner.UniformVariants.IgnoreQueryFilters().CountAsync(v => v.UniformItemId == item.Id && v.IsDeleted))
            .Should().Be(2);
        (await owner.InventoryItems.IgnoreQueryFilters().CountAsync(i => i.SchoolId == EcoleA && !i.IsDeleted))
            .Should().Be(2, "les lots restent actifs : archiver un modèle n'efface pas le stock physique");
    }

    [Fact]
    public async Task Archiving_An_Item_With_A_Stale_Token_Is_Refused()
    {
        var item = await CreateItemAsync("Polo bleu");

        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            await new UpdateUniformItemCommandHandler(ctx).Handle(
                new UpdateUniformItemCommand
                {
                    Id = item.Id, Name = "Polo bleu", Notes = "mis à jour", Kind = UniformKind.Uniforme,
                    Gender = UniformGender.Mixte, QuantityIncluded = 1, RowVersion = item.RowVersion
                },
                CancellationToken.None);
        }

        await using var stale = _db.NewAppContext(EcoleA);
        var act = () => new DeleteUniformItemCommandHandler(stale, new TestCurrentUser(Acteur)).Handle(
            new DeleteUniformItemCommand { Id = item.Id, RowVersion = item.RowVersion }, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task An_Inventory_Lot_Used_By_A_Size_Cannot_Be_Archived_From_The_Inventory()
    {
        var item = await CreateItemAsync("Polo bleu");
        var variant = await CreateVariantAsync(item.Id, "10 ans");

        uint lotVersion;
        await using (var read = _db.NewAppContext(EcoleA))
        {
            lotVersion = await read.InventoryItems.AsNoTracking().Where(i => i.Id == variant.InventoryItemId)
                .Select(i => EF.Property<uint>(i, "xmin")).SingleAsync();
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var act = () => new DeleteInventoryItemCommandHandler(ctx, new TestCurrentUser(Acteur)).Handle(
            new DeleteInventoryItemCommand(variant.InventoryItemId, lotVersion), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("UNIFORM_LOT_IN_USE");
    }

    // ------------------------------------------------------------------ lecture

    [Fact]
    public async Task The_Catalogue_Lists_Own_Items_With_Sizes_In_Order_And_Live_Stock()
    {
        var item = await CreateItemAsync("Polo bleu", FraisA);
        await CreateVariantAsync(item.Id, "12 ans", sortOrder: 2);
        var small = await CreateVariantAsync(item.Id, "8 ans", sortOrder: 1);
        await CreateItemAsync("Jupe grise", gender: UniformGender.Fille);

        // Une entrée de stock saisie depuis l'écran Inventaire (ici par le propriétaire).
        await using (var owner = _db.NewOwnerContext())
        {
            var lot = await owner.InventoryItems.IgnoreQueryFilters().SingleAsync(i => i.Id == small.InventoryItemId);
            lot.QuantityTotal = 12;
            lot.QuantityAvailable = 9;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var catalogue = await ListAsync(EcoleA);

        catalogue.Select(i => i.Name).Should().Equal("Jupe grise", "Polo bleu");

        var polo = catalogue.Single(i => i.Name == "Polo bleu");
        polo.FeeCategoryName.Should().Be("Uniforme");
        polo.Variants.Select(v => v.SizeLabel).Should().Equal("8 ans", "12 ans");
        polo.Variants[0].QuantityTotal.Should().Be(12);
        polo.Variants[0].QuantityAvailable.Should().Be(9);

        catalogue.Select(i => i.Id).Should().NotContain(ModeleB, "le catalogue de l'école B reste invisible");
        (await ListAsync(EcoleB)).Should().ContainSingle(i => i.Id == ModeleB);
    }
}
