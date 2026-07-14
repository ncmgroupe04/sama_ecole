using System.Security.Cryptography;
using System.Text;

namespace SamaEcole.Application.Auth;

/// <summary>
/// Fabrique des refresh tokens (ticket JGK-A04).
///
/// Le token est tiré du CSPRNG (256 bits) : il n'est ni devinable, ni dérivable de l'utilisateur.
/// Seul son SHA-256 est stocké en base — le clair ne quitte jamais la réponse HTTP. Un SHA-256 nu
/// suffit ici (contrairement à un mot de passe) : l'entrée est déjà à haute entropie, une attaque
/// par dictionnaire n'a aucune prise, et le hachage doit rester rapide car il est fait à chaque refresh.
/// </summary>
public static class RefreshTokenFactory
{
    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public static string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(digest);
    }
}