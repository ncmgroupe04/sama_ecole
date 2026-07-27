using FluentAssertions;
using SamaEcole.Infrastructure.Multitenancy;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace SamaEcole.UnitTests.Multitenancy;

/// <summary>
/// Étape 5 — RunAsSchoolAsync est le SEUL moyen pour une tâche de fond (DebtorAgingHostedService)
/// d'obtenir un SchoolId hors requête HTTP. Ces tests verrouillent les deux garanties dont dépend
/// toute l'isolation multi-tenant du job : (1) sans override, le comportement par défaut reste
/// "fail closed" (aucun tenant) ; (2) l'override retombe TOUJOURS à ce défaut à la sortie, même si le
/// travail lève une exception — sinon un tenant resterait actif pour un travail ultérieur sans
/// rapport, sur le même thread pool.
/// </summary>
public class TenantProviderTests
{
    private static TenantProvider NewProvider()
    {
        // HttpContext toujours null : reproduit exactement la situation d'un service hébergé, qui ne
        // s'exécute jamais dans une requête HTTP.
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        return new TenantProvider(accessor);
    }

    [Fact]
    public void Without_Override_And_Without_HttpContext_CurrentSchoolId_Is_Null()
    {
        NewProvider().CurrentSchoolId.Should().BeNull();
    }

    [Fact]
    public async Task RunAsSchoolAsync_Exposes_The_Given_School_Inside_The_Callback()
    {
        var provider = NewProvider();
        var schoolId = Guid.NewGuid();

        var observed = await TenantProvider.RunAsSchoolAsync(schoolId, async () =>
        {
            await Task.Yield();
            return provider.CurrentSchoolId;
        });

        observed.Should().Be(schoolId);
    }

    [Fact]
    public async Task RunAsSchoolAsync_Resets_To_Null_After_The_Callback_Completes()
    {
        var provider = NewProvider();

        await TenantProvider.RunAsSchoolAsync(Guid.NewGuid(), () => Task.FromResult(true));

        provider.CurrentSchoolId.Should().BeNull("l'override ne doit jamais survivre à son propre appel");
    }

    [Fact]
    public async Task RunAsSchoolAsync_Resets_To_Null_Even_When_The_Callback_Throws()
    {
        var provider = NewProvider();

        var act = () => TenantProvider.RunAsSchoolAsync<int>(Guid.NewGuid(), () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        provider.CurrentSchoolId.Should().BeNull("un travail en échec ne doit pas laisser un tenant actif pour la suite");
    }

    /// <summary>
    /// Le risque central du mécanisme (AGENTS.md règle #2) : deux écoles traitées SÉQUENTIELLEMENT
    /// dans la même boucle ne doivent jamais se voir l'une l'autre. Chaque itération de
    /// DebtorAgingHostedService pose et retire son override avant de passer à l'école suivante.
    /// </summary>
    [Fact]
    public async Task Sequential_RunAsSchoolAsync_Calls_For_Different_Schools_Never_Leak_Into_Each_Other()
    {
        var provider = NewProvider();
        var schoolA = Guid.NewGuid();
        var schoolB = Guid.NewGuid();

        var observedA = await TenantProvider.RunAsSchoolAsync(schoolA, () => Task.FromResult(provider.CurrentSchoolId));
        var observedBetween = provider.CurrentSchoolId;
        var observedB = await TenantProvider.RunAsSchoolAsync(schoolB, () => Task.FromResult(provider.CurrentSchoolId));

        observedA.Should().Be(schoolA);
        observedBetween.Should().BeNull();
        observedB.Should().Be(schoolB);
    }
}
