using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence.Seed;
using Xunit;

namespace SamaEcole.IntegrationTests.Subscriptions;

/// <summary>
/// Les écoles de démonstration doivent pouvoir créer des élèves : le quota étant fail-closed, le semis de
/// développement amorce une souscription commerciale Active et illimitée (comme la migration de reprise).
/// </summary>
public class DbSeederTenantSubscriptionTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Seeded_Demo_Schools_Get_One_Active_Unlimited_Subscription_Even_When_Seeded_Twice()
    {
        var hasher = new IdentityPasswordHasher();

        await DbSeeder.SeedAsync(_db.OwnerConnectionString, hasher);
        await DbSeeder.SeedAsync(_db.OwnerConnectionString, hasher);

        await using var owner = _db.NewOwnerContext();
        var rows = await owner.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        rows.Select(r => r.SchoolId).Should().BeEquivalentTo([DbSeeder.BaobabsId, DbSeeder.TerangaId], "une ligne par école, jamais de doublon");
        rows.Should().OnlyContain(r =>
            r.Status == TenantSubscriptionStatus.Active
            && r.MaxStudentLimit == int.MaxValue
            && r.SoftQuotaLimit == int.MaxValue
            && r.IsPedagogyEnabled && r.IsFinanceEnabled);
    }
}
