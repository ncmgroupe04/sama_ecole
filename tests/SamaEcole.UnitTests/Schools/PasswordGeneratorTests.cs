using FluentAssertions;
using SamaEcole.Infrastructure.Security;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Ticket JGK-B01 — le mot de passe initial du Directeur doit satisfaire la politique de
/// docs/Volume_7_Security.md §2, sans quoi notre propre login le refuserait.
/// </summary>
public class PasswordGeneratorTests
{
    private readonly PasswordGenerator _generator = new();

    [Fact]
    public void Generated_Passwords_Should_Satisfy_The_Security_Policy()
    {
        // 200 tirages : une classe de caractères manquante est un événement RARE. Un seul tirage
        // passerait au vert par chance et laisserait le bug filer en production.
        for (var i = 0; i < 200; i++)
        {
            var password = _generator.Generate();

            password.Length.Should().BeGreaterThanOrEqualTo(12);
            password.Should().Match(p => p.Any(char.IsUpper), "il faut au moins une majuscule");
            password.Should().Match(p => p.Any(char.IsLower), "il faut au moins une minuscule");
            password.Should().Match(p => p.Any(char.IsDigit), "il faut au moins un chiffre");
            password.Should().Match(p => p.Any(c => "!@#$%*?-+".Contains(c)), "il faut au moins un caractère spécial");
        }
    }

    [Fact]
    public void Two_Generated_Passwords_Should_Never_Be_The_Same()
    {
        var passwords = Enumerable.Range(0, 100).Select(_ => _generator.Generate()).ToList();

        passwords.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Ambiguous_Characters_Should_Be_Excluded()
    {
        // Le mot de passe est recopié à la main depuis un e-mail, souvent sur un téléphone :
        // O/0 et l/1/I y sont une source d'échecs de connexion inexplicables.
        for (var i = 0; i < 100; i++)
        {
            _generator.Generate().Should().NotContainAny("O", "0", "l", "1", "I");
        }
    }

    [Fact]
    public void The_First_Characters_Should_Not_Always_Follow_The_Same_Class_Order()
    {
        // Sans brassage, le mot de passe commencerait TOUJOURS par majuscule/minuscule/chiffre/spécial —
        // quatre positions offertes à qui tenterait de deviner.
        var firstChars = Enumerable.Range(0, 50).Select(_ => _generator.Generate()[0]).ToList();

        firstChars.Should().Contain(c => !char.IsUpper(c),
            "la première position ne doit pas être systématiquement une majuscule");
    }
}
