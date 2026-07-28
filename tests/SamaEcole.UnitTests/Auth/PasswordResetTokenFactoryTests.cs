using FluentAssertions;
using SamaEcole.Application.Auth;
using Xunit;

namespace SamaEcole.UnitTests.Auth;

public class PasswordResetTokenFactoryTests
{
    /// <summary>
    /// Le jeton voyage dans une URL envoyée par e-mail : aucun caractère du base64 standard
    /// (« + », « / », « = ») ne doit y figurer. Un « + » transformé en espace par un client de
    /// messagerie rendrait le lien invalide chez une partie seulement des utilisateurs — panne
    /// intermittente et particulièrement pénible à diagnostiquer.
    /// </summary>
    [Fact]
    public void Create_Produces_A_Url_Safe_Token()
    {
        for (var i = 0; i < 200; i++)
        {
            var token = PasswordResetTokenFactory.Create();

            token.Should().NotContainAny("+", "/", "=");
            Uri.EscapeDataString(token).Should().Be(token, "un jeton déjà sûr en URL n'est pas modifié par l'échappement");
        }
    }

    /// <summary>256 bits de CSPRNG : deux demandes ne doivent jamais produire le même jeton.</summary>
    [Fact]
    public void Create_Produces_A_Distinct_Token_Every_Time()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => PasswordResetTokenFactory.Create()).ToList();

        tokens.Should().OnlyHaveUniqueItems();
    }

    /// <summary>Le condensat est déterministe : c'est ce qui permet de retrouver le jeton en base.</summary>
    [Fact]
    public void Hash_Is_Deterministic()
    {
        var token = PasswordResetTokenFactory.Create();

        PasswordResetTokenFactory.Hash(token).Should().Be(PasswordResetTokenFactory.Hash(token));
    }

    /// <summary>
    /// Le clair ne doit JAMAIS être dérivable du stocké : une fuite de la base ne doit pas permettre
    /// de forger un lien valide. Le condensat diffère donc du jeton, et deux jetons donnent deux
    /// condensats.
    /// </summary>
    [Fact]
    public void Hash_Never_Returns_The_Token_Itself()
    {
        var first = PasswordResetTokenFactory.Create();
        var second = PasswordResetTokenFactory.Create();

        PasswordResetTokenFactory.Hash(first).Should().NotBe(first);
        PasswordResetTokenFactory.Hash(first).Should().NotBe(PasswordResetTokenFactory.Hash(second));
    }

    /// <summary>Le condensat tient dans la colonne TokenHash (varchar(64)) — SHA-256 en base64 = 44 caractères.</summary>
    [Fact]
    public void Hash_Fits_The_Database_Column()
    {
        PasswordResetTokenFactory.Hash(PasswordResetTokenFactory.Create()).Length.Should().BeLessThanOrEqualTo(64);
    }
}
