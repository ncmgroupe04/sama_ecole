using System.Security.Cryptography;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Infrastructure.Security;

/// <summary>
/// Mot de passe initial du Directeur (ticket JGK-B01), conforme à docs/Volume_7_Security.md §2 :
/// 8 caractères minimum, au moins une majuscule, une minuscule, un chiffre et un caractère spécial.
///
/// Tiré du CSPRNG (RandomNumberGenerator), jamais de Random : un mot de passe prédictible depuis
/// l'horloge annulerait tout l'intérêt de le générer.
/// </summary>
public class PasswordGenerator : IPasswordGenerator
{
    private const int Length = 16;

    // Caractères ambigus volontairement exclus (O/0, l/1/I) : ce mot de passe est recopié à la main
    // depuis un e-mail, souvent depuis un téléphone.
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!@#$%*?-+";

    private const string All = Uppercase + Lowercase + Digits + Special;

    public string Generate()
    {
        // Une classe imposée d'entrée de jeu : un tirage purement aléatoire pourrait, rarement,
        // ne produire aucun chiffre — et le mot de passe serait alors refusé par notre propre
        // politique de sécurité.
        var characters = new List<char>
        {
            Pick(Uppercase),
            Pick(Lowercase),
            Pick(Digits),
            Pick(Special)
        };

        while (characters.Count < Length)
        {
            characters.Add(Pick(All));
        }

        // Sans ce brassage, les quatre premiers caractères suivraient toujours le même ordre de
        // classes — une information offerte à qui tenterait de deviner.
        return new string(Shuffle(characters));
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static char[] Shuffle(List<char> characters)
    {
        var array = characters.ToArray();
        RandomNumberGenerator.Shuffle(array.AsSpan());
        return array;
    }
}
