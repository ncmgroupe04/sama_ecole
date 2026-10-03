using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Conception soft delete 2026-10-01 §4.1 : les identités réutilisables sont protégées par un index unique
/// PARTIEL (<c>WHERE "IsDeleted" = false</c>) — plusieurs tombstones pour une même identité, une seule ligne
/// active. Vérifié sur PostgreSQL réel, pas seulement sur les métadonnées EF.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SoftDeleteUniqueIndexTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("31111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("32222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    public static TheoryData<string> Identities => new() { "Building", "FeeCategory", "Mention" };

    private static AuditableEntity Make(string kind, Guid school) => kind switch
    {
        "Building" => new Building { SchoolId = school, Name = "Bloc A" },
        "FeeCategory" => new FeeCategory { SchoolId = school, Name = "Cantine" },
        "Mention" => new Mention { SchoolId = school, Label = "Bien", MinAverage = 14 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Identities))]
    public async Task Several_Tombstones_And_One_Active_Row_Can_Share_An_Identity(string kind)
    {
        await using var owner = _db.NewOwnerContext();

        for (var i = 0; i < 2; i++)
        {
            var archived = Make(kind, EcoleA);
            owner.Add(archived);
            await owner.SaveChangesAsync(CancellationToken.None);
            archived.SoftDelete("test");
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        owner.Add(Make(kind, EcoleA));
        await owner.Invoking(c => c.SaveChangesAsync(CancellationToken.None)).Should().NotThrowAsync(
            "plusieurs tombstones n'empêchent pas la création d'une ligne active");
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public async Task A_Second_Active_Row_With_The_Same_Identity_Is_Refused(string kind)
    {
        await using var owner = _db.NewOwnerContext();
        owner.Add(Make(kind, EcoleA));
        await owner.SaveChangesAsync(CancellationToken.None);

        owner.Add(Make(kind, EcoleA));
        await owner.Invoking(c => c.SaveChangesAsync(CancellationToken.None))
            .Should().ThrowAsync<DuplicateRecordException>();
    }

    [Theory]
    [MemberData(nameof(Identities))]
    public async Task Two_Tenants_Can_Use_The_Same_Identity(string kind)
    {
        await using var owner = _db.NewOwnerContext();
        owner.Add(Make(kind, EcoleA));
        owner.Add(Make(kind, EcoleB));

        await owner.Invoking(c => c.SaveChangesAsync(CancellationToken.None)).Should().NotThrowAsync();
    }
}
