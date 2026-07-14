using FluentAssertions;
using SamaEcole.Application.Auth;
using SamaEcole.Infrastructure.Security;
using Xunit;

namespace SamaEcole.UnitTests.Auth;

/// <summary>
/// Ticket JGK-A04 — critère « mot de passe stocké hashé (Identity par défaut) »,
/// docs/Volume_7_Security.md §6.
/// </summary>
public class PasswordHasherTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hash_Should_Never_Contain_The_Password_In_Clear()
    {
        const string password = "Motdepasse!Solide2026";

        var hash = _hasher.Hash(password);

        hash.Should().NotContain(password);
        hash.Should().NotBe(password);
    }

    [Fact]
    public void The_Same_Password_Should_Hash_Differently_Twice()
    {
        // Identity tire un sel aléatoire par mot de passe : deux comptes avec le même mot de passe
        // n'ont pas le même hash, ce qui interdit les attaques par table précalculée.
        const string password = "Motdepasse!Solide2026";

        _hasher.Hash(password).Should().NotBe(_hasher.Hash(password));
    }

    [Fact]
    public void Verify_Should_Accept_The_Right_Password_And_Reject_The_Others()
    {
        var hash = _hasher.Hash("Motdepasse!Solide2026");

        _hasher.Verify(hash, "Motdepasse!Solide2026").Should().BeTrue();
        _hasher.Verify(hash, "motdepasse!solide2026").Should().BeFalse("le mot de passe est sensible à la casse");
        _hasher.Verify(hash, "autre").Should().BeFalse();
    }

    [Fact]
    public void Verify_Should_Reject_A_Malformed_Hash_Instead_Of_Crashing()
    {
        // Un hash corrompu en base ne doit pas faire tomber le login en 500 : il doit simplement
        // échouer l'authentification.
        var act = () => _hasher.Verify("pas-un-hash-valide", "Motdepasse!Solide2026");

        act.Should().NotThrow();
        _hasher.Verify("pas-un-hash-valide", "Motdepasse!Solide2026").Should().BeFalse();
    }

    [Fact]
    public void Refresh_Tokens_Should_Be_Random_And_Stored_Only_As_A_Digest()
    {
        var first = RefreshTokenFactory.Create();
        var second = RefreshTokenFactory.Create();

        first.Should().NotBe(second, "chaque refresh token doit être tiré du CSPRNG");

        // Le condensat est déterministe (indispensable pour retrouver le token en base)…
        RefreshTokenFactory.Hash(first).Should().Be(RefreshTokenFactory.Hash(first));
        // …mais ne laisse pas fuiter le token lui-même.
        RefreshTokenFactory.Hash(first).Should().NotBe(first);
    }
}
