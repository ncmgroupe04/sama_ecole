using FluentAssertions;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace SamaEcole.UnitTests.Auth;

/// <summary>
/// Ticket JGK-A04 : le JWT doit porter sub, schoolId et role (AGENTS.md règle #10).
/// C'est le schoolId de ce token — et lui seul — qui pilotera la RLS PostgreSQL.
/// </summary>
public class JwtTokenGeneratorTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SchoolId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static JwtTokenGenerator NewGenerator() =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "https://api.sama-ecole.sn",
            Audience = "sama-ecole-clients",
            SigningKey = "cle_de_test_suffisamment_longue_pour_hmac_sha256_0123456789",
            AccessTokenMinutes = 15
        }),
        TimeProvider.System);

    private static JsonWebToken Decode(string token) => new(token);

    [Fact]
    public void Token_Should_Carry_Sub_SchoolId_And_Role()
    {
        var token = NewGenerator().Generate(UserId, SchoolId, Role.Directeur);

        var claims = Decode(token.Value).Claims.ToDictionary(c => c.Type, c => c.Value);

        claims["sub"].Should().Be(UserId.ToString());
        claims["schoolId"].Should().Be(SchoolId.ToString());
        claims["role"].Should().Be(nameof(Role.Directeur));
    }

    [Fact]
    public void SuperAdmin_Token_Should_Carry_No_SchoolId_At_All()
    {
        // Un Super Admin n'appartient à aucune école. Le claim doit être ABSENT, pas vide :
        // TenantProvider renvoie alors null, et la RLS ne laisse passer aucune donnée d'école.
        var token = NewGenerator().Generate(UserId, schoolId: null, Role.SuperAdmin);

        var claims = Decode(token.Value).Claims.Select(c => c.Type);

        claims.Should().NotContain("schoolId");
    }

    [Fact]
    public void Token_Should_Expire_And_Report_Its_Lifetime()
    {
        var token = NewGenerator().Generate(UserId, SchoolId, Role.Enseignant);

        token.ExpiresInSeconds.Should().Be(15 * 60);
        Decode(token.Value).ValidTo.Should().BeAfter(DateTime.UtcNow);
        Decode(token.Value).ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Generating_Without_A_Signing_Key_Should_Fail_Loudly()
    {
        // Mieux vaut une exception explicite qu'un token signé avec une clé vide, qui serait forgeable.
        var generator = new JwtTokenGenerator(
            Options.Create(new JwtOptions { SigningKey = "" }), TimeProvider.System);

        var act = () => generator.Generate(UserId, SchoolId, Role.Directeur);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*");
    }
}
