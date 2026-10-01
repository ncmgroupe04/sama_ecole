using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Application.Finance.Queries.GetFeeCategories;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Frais optionnels, Tâche 1 — <see cref="FeeCategory.IsOptional"/> doit réellement round-tripper en
/// base (colonne IsOptional), se modifier après coup, et rester cloisonné par école.
/// </summary>
[Trait("Category", "MultiTenant")]
public class OptionalFeeCategoryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("33333333-2222-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("33333333-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École Optionnels A" },
            new School { Id = EcoleB, Name = "École Optionnels B" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<CreateFeeCategoryResult> CreateAsync(Guid school, CreateFeeCategoryCommand command)
    {
        await using var db = _db.NewAppContext(school);
        return await new CreateFeeCategoryCommandHandler(db, new StubTenantProvider(school))
            .Handle(command, CancellationToken.None);
    }

    [Fact]
    public async Task Create_Persists_IsOptional()
    {
        var result = await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Uniforme", IsOptional = true });

        result.IsOptional.Should().BeTrue();

        await using var check = _db.NewAppContext(EcoleA);
        (await check.FeeCategories.SingleAsync(c => c.Id == result.Id)).IsOptional
            .Should().BeTrue("la colonne IsOptional doit réellement porter la valeur, pas seulement le DTO renvoyé");
    }

    [Fact]
    public async Task Create_Without_The_Flag_Stays_Mandatory()
    {
        var result = await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Inscription" });

        result.IsOptional.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Toggles_IsOptional_Both_Ways()
    {
        var created = await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Tenue de sport" });

        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(created.Id, IsOptional: true), CancellationToken.None);
        }

        await using (var check = _db.NewAppContext(EcoleA))
        {
            (await check.FeeCategories.SingleAsync(c => c.Id == created.Id)).IsOptional.Should().BeTrue();
        }

        await using (var db = _db.NewAppContext(EcoleA))
        {
            await new UpdateFeeCategoryCommandHandler(db)
                .Handle(new UpdateFeeCategoryCommand(created.Id, IsOptional: false), CancellationToken.None);
        }

        await using var checkAgain = _db.NewAppContext(EcoleA);
        (await checkAgain.FeeCategories.SingleAsync(c => c.Id == created.Id)).IsOptional.Should().BeFalse();
    }

    [Fact]
    public async Task Update_Refuses_To_Make_A_Boarding_Category_Optional()
    {
        var pension = await CreateAsync(EcoleA, new CreateFeeCategoryCommand
        {
            Name = "Pension", IsRecurring = true, IsBoardingFee = true
        });

        await using var db = _db.NewAppContext(EcoleA);
        var act = () => new UpdateFeeCategoryCommandHandler(db)
            .Handle(new UpdateFeeCategoryCommand(pension.Id, IsOptional: true), CancellationToken.None);

        await act.Should().ThrowAsync<SamaEcole.Application.Common.Exceptions.ValidationException>();
    }

    [Fact]
    public async Task Update_Cannot_Reach_Another_Schools_Category()
    {
        var chezB = await CreateAsync(EcoleB, new CreateFeeCategoryCommand { Name = "Uniforme" });

        await using var db = _db.NewAppContext(EcoleA);
        var act = () => new UpdateFeeCategoryCommandHandler(db)
            .Handle(new UpdateFeeCategoryCommand(chezB.Id, IsOptional: true), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();

        await using var check = _db.NewAppContext(EcoleB);
        (await check.FeeCategories.SingleAsync(c => c.Id == chezB.Id)).IsOptional.Should().BeFalse();
    }

    [Fact]
    public async Task List_Exposes_IsOptional()
    {
        await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Uniforme", IsOptional = true });
        await CreateAsync(EcoleA, new CreateFeeCategoryCommand { Name = "Scolarité", IsRecurring = true });

        await using var db = _db.NewAppContext(EcoleA);
        var list = await new GetFeeCategoriesQueryHandler(db)
            .Handle(new GetFeeCategoriesQuery(), CancellationToken.None);

        list.Single(c => c.Name == "Uniforme").IsOptional.Should().BeTrue();
        list.Single(c => c.Name == "Scolarité").IsOptional.Should().BeFalse();
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
