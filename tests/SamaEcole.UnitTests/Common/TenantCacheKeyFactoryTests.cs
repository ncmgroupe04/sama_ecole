using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Infrastructure.Multitenancy;
using NSubstitute;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>
/// Audit BOLA/IDOR (recommandation 2/3 : anticiper le cache Redis). Aucun cache applicatif n'est
/// encore consommé en code, mais la garantie « toute clé de cache porte le SchoolId » (Volume_3_DDS.md
/// §2.5) doit être vraie dès la première utilisation — donc testée dès maintenant.
/// </summary>
public class TenantCacheKeyFactoryTests
{
    [Fact]
    public void BuildKey_Should_Prefix_The_Key_With_The_Current_Tenant()
    {
        var schoolId = Guid.NewGuid();
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.CurrentSchoolId.Returns(schoolId);
        var factory = new TenantCacheKeyFactory(tenantProvider);

        var key = factory.BuildKey("grading-scale");

        key.Should().Be($"school:{schoolId}:grading-scale");
    }

    [Fact]
    public void BuildKey_Should_Never_Return_The_Same_Key_For_Two_Different_Schools()
    {
        var tenantProviderA = Substitute.For<ITenantProvider>();
        tenantProviderA.CurrentSchoolId.Returns(Guid.NewGuid());
        var tenantProviderB = Substitute.For<ITenantProvider>();
        tenantProviderB.CurrentSchoolId.Returns(Guid.NewGuid());

        var keyA = new TenantCacheKeyFactory(tenantProviderA).BuildKey("grading-scale");
        var keyB = new TenantCacheKeyFactory(tenantProviderB).BuildKey("grading-scale");

        keyA.Should().NotBe(keyB, "deux écoles ne doivent jamais pouvoir se lire l'une l'autre via une clé de cache partagée");
    }

    [Fact]
    public void BuildKey_Should_Fail_Closed_When_No_Tenant_Is_Resolved()
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.CurrentSchoolId.Returns((Guid?)null);
        var factory = new TenantCacheKeyFactory(tenantProvider);

        var act = () => factory.BuildKey("grading-scale");

        act.Should().Throw<UnauthorizedAccessException>("une clé de cache 'globale' accidentelle serait pire que l'absence de cache");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_Should_Reject_A_Blank_Logical_Key(string? blankKey)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.CurrentSchoolId.Returns(Guid.NewGuid());
        var factory = new TenantCacheKeyFactory(tenantProvider);

        var act = () => factory.BuildKey(blankKey!);

        act.Should().Throw<ArgumentException>();
    }
}
