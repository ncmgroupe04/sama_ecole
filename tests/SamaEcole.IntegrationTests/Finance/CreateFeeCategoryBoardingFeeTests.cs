using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Module Internat, Task 9 — <see cref="FeeCategory.IsBoardingFee"/> doit pouvoir être positionné à
/// la création d'une catégorie de frais, et round-tripper réellement en base (pas seulement dans le
/// DTO retourné) : c'est ce qui distingue une pension (Task 1/2, colonne déjà migrée) d'un frais
/// scolaire ordinaire pour Task 12 (checkbox Fees) et pour le calcul de pension à l'inscription.
/// </summary>
[Trait("Category", "MultiTenant")]
public class CreateFeeCategoryBoardingFeeTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-1111-1111-1111-111111111111");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = EcoleA, Name = "École Internat A" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Sets_IsBoardingFee_When_Requested_And_Persists_It()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = new CreateFeeCategoryCommandHandler(db, new StubTenantProvider(EcoleA));

        var result = await handler.Handle(
            new CreateFeeCategoryCommand { Name = "Pension", IsRecurring = true, IsBoardingFee = true },
            CancellationToken.None);

        result.IsBoardingFee.Should().BeTrue();

        await using var check = _db.NewAppContext(EcoleA);
        var persisted = await check.FeeCategories.SingleAsync(c => c.Id == result.Id);
        persisted.IsBoardingFee.Should().BeTrue("la colonne is_boarding_fee doit réellement porter la valeur, pas seulement le DTO renvoyé");
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
