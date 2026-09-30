using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Soft delete et unicité (AGENTS.md règle #6) : un nom supprimé doit pouvoir être RECRÉÉ, autant de fois
/// qu'on veut. Les anciens index d'unicité portaient <c>IsDeleted</c> DANS leur clé — (école, nom, IsDeleted) —
/// ce qui autorise une seule ligne supprimée par nom : la seconde suppression du même nom entrait en collision
/// (création → suppression → recréation → suppression = 23505, remonté au client en 409). Un index PARTIEL
/// (<c>WHERE "IsDeleted" = false</c>) ne contraint que les lignes vivantes, et c'est tout ce que veut le métier.
/// </summary>
public class SoftDeleteNameReuseTests(SoftDeleteNameReuseTests.Fixture fixture) : IClassFixture<SoftDeleteNameReuseTests.Fixture>
{
    public sealed class Fixture : IAsyncLifetime
    {
        public RlsTestDatabase Db { get; } = new();

        public Guid EcoleId { get; } = Guid.Parse("55555555-0000-0000-0000-000000000001");

        public async Task InitializeAsync()
        {
            await Db.InitializeAsync();
            await using var owner = Db.NewOwnerContext();
            owner.Schools.Add(new School { Id = EcoleId, Name = "École Soft Delete" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        public Task DisposeAsync() => Db.DisposeAsync().AsTask();
    }

    private static T Make<T>(T entity, bool deleted) where T : AuditableEntity
    {
        if (deleted) entity.SoftDelete("test");
        return entity;
    }

    private async Task AddAsync(AuditableEntity entity)
    {
        await using var owner = fixture.Db.NewOwnerContext();
        owner.Add(entity);
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Supprimé, recréé, supprimé, recréé : tout passe. Deux lignes VIVANTES identiques : refusé.</summary>
    private async Task AssertNameCanBeReusedAsync(Func<bool, AuditableEntity> make)
    {
        await AddAsync(make(true));   // 1re vie : créée puis supprimée
        await AddAsync(make(true));   // 2e vie : recréée puis supprimée — c'est ici que l'ancien index cassait
        await AddAsync(make(false));  // 3e vie : recréée et vivante

        var duplicate = async () => await AddAsync(make(false));
        await duplicate.Should().ThrowAsync<Exception>("deux lignes VIVANTES de même nom restent interdites");
    }

    [Fact]
    public Task A_Classroom_Name_Can_Be_Reused_After_Deletion() => AssertNameCanBeReusedAsync(deleted =>
        Make(new Classroom { SchoolId = fixture.EcoleId, Name = "CM2 Réutilisée", Level = "Primaire", Capacity = 40 }, deleted));

    [Fact]
    public Task A_Fee_Category_Name_Can_Be_Reused_After_Deletion() => AssertNameCanBeReusedAsync(deleted =>
        Make(new FeeCategory { SchoolId = fixture.EcoleId, Name = "Uniforme Réutilisé" }, deleted));

    [Fact]
    public Task A_Mention_Label_Can_Be_Reused_After_Deletion() => AssertNameCanBeReusedAsync(deleted =>
        Make(new Mention { SchoolId = fixture.EcoleId, Label = "Excellent Réutilisé", MinAverage = 16m }, deleted));

    [Fact]
    public Task A_Building_Name_Can_Be_Reused_After_Deletion() => AssertNameCanBeReusedAsync(deleted =>
        Make(new Building { SchoolId = fixture.EcoleId, Name = "Bâtiment Réutilisé" }, deleted));

    [Fact]
    public Task An_Inventory_Category_Name_Can_Be_Reused_After_Deletion() => AssertNameCanBeReusedAsync(deleted =>
        Make(new InventoryCategory { SchoolId = fixture.EcoleId, Name = "Mobilier Réutilisé" }, deleted));

    [Fact]
    public Task A_School_Year_Label_Can_Be_Reused_After_Deletion() => AssertNameCanBeReusedAsync(deleted =>
        Make(new SchoolYear
        {
            SchoolId = fixture.EcoleId, Label = "2031-2032",
            StartDate = new DateOnly(2031, 10, 1), EndDate = new DateOnly(2032, 6, 30), IsActive = false
        }, deleted));

    [Fact]
    public async Task A_Class_Fee_Can_Be_Set_Again_After_Deletion()
    {
        var categoryId = Guid.NewGuid();
        var classroomId = Guid.NewGuid();
        await AddAsync(new FeeCategory { Id = categoryId, SchoolId = fixture.EcoleId, Name = "Frais Barème" });
        await AddAsync(new Classroom { Id = classroomId, SchoolId = fixture.EcoleId, Name = "6e Barème", Level = "Collège", Capacity = 40 });

        await AssertNameCanBeReusedAsync(deleted =>
            Make(new ClassFee { SchoolId = fixture.EcoleId, FeeCategoryId = categoryId, ClassroomId = classroomId, Amount = 10_000m }, deleted));
    }

    // ------------------------------------------------------------------ Garde-fou structurel (sans base)

    private sealed class NoTenant : ITenantProvider
    {
        public Guid? CurrentSchoolId => null;
    }

    /// <summary>
    /// Les dix index d'unicité « nom par école » à supprimer-recréer : chacun doit être PARTIEL sur les lignes
    /// vivantes, et ne plus porter <c>IsDeleted</c> dans sa clé (redondant avec le filtre, et c'est lui qui
    /// autorisait au plus une ligne supprimée par nom).
    /// </summary>
    public static IEnumerable<object[]> NameScopedEntities =>
    [
        [typeof(Classroom)], [typeof(FeeCategory)], [typeof(Mention)], [typeof(Building)], [typeof(Room)],
        [typeof(InventoryCategory)], [typeof(ClassFee)], [typeof(SchoolYear)], [typeof(Term)], [typeof(UserSchool)]
    ];

    [Theory]
    [MemberData(nameof(NameScopedEntities))]
    public void The_Unique_Name_Index_Only_Constrains_Live_Rows(Type entityType)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=model-only").Options;
        using var context = new ApplicationDbContext(options, new NoTenant(), NullLogger<ApplicationDbContext>.Instance);

        var indexes = context.Model.FindEntityType(entityType)!.GetIndexes().Where(i => i.IsUnique).ToList();

        // L'index « nom par école » = celui dont le filtre parle de IsDeleted (les autres, ex. SchoolYear.IsActive, gardent le leur).
        var nameIndex = indexes.SingleOrDefault(i => i.GetFilter() == "\"IsDeleted\" = false");
        nameIndex.Should().NotBeNull($"{entityType.Name} doit avoir un index d'unicité partiel sur les lignes vivantes");
        nameIndex!.Properties.Select(p => p.Name).Should().NotContain(nameof(AuditableEntity.IsDeleted));
    }
}
