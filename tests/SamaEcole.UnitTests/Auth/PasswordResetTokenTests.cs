using FluentAssertions;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Auth;

/// <summary>
/// Les quatre raisons pour lesquelles un lien de réinitialisation cesse d'être valable. Chacune couvre
/// un scénario d'abus réel — ce n'est pas de la combinatoire pour la forme.
/// </summary>
public class PasswordResetTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    private static PasswordResetToken Token(
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? usedAt = null,
        DateTimeOffset? revokedAt = null) => new()
    {
        UserId = Guid.NewGuid(),
        TokenHash = "hash",
        ExpiresAt = expiresAt ?? Now.AddMinutes(20),
        UsedAt = usedAt,
        RevokedAt = revokedAt
    };

    [Fact]
    public void A_Fresh_Token_Is_Usable()
    {
        Token().IsUsable(Now).Should().BeTrue();
    }

    /// <summary>Un lien resté dans une boîte mail consultée plus tard ne doit plus rien ouvrir.</summary>
    [Fact]
    public void An_Expired_Token_Is_Not_Usable()
    {
        Token(expiresAt: Now.AddSeconds(-1)).IsUsable(Now).Should().BeFalse();
    }

    /// <summary>Usage unique : le lien ne se rejoue pas, même dans sa fenêtre de validité.</summary>
    [Fact]
    public void A_Consumed_Token_Is_Not_Usable()
    {
        Token(usedAt: Now.AddMinutes(-1)).IsUsable(Now).Should().BeFalse();
    }

    /// <summary>Une nouvelle demande périme la précédente : un seul lien vivant à la fois par compte.</summary>
    [Fact]
    public void A_Revoked_Token_Is_Not_Usable()
    {
        Token(revokedAt: Now.AddMinutes(-1)).IsUsable(Now).Should().BeFalse();
    }

    /// <summary>La borne est stricte : à la seconde d'expiration, le jeton n'est déjà plus utilisable.</summary>
    [Fact]
    public void Expiry_Is_Exclusive_At_The_Exact_Instant()
    {
        Token(expiresAt: Now).IsUsable(Now).Should().BeFalse();
    }
}
