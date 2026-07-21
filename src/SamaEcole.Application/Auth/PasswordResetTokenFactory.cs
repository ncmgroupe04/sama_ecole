using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SamaEcole.Application.Auth;

/// <summary>
/// Fabrique les jetons de réinitialisation de mot de passe.
///
/// Même principe que <see cref="RefreshTokenFactory"/> — 256 bits de CSPRNG, seul le SHA-256 est
/// stocké — avec UNE différence qui justifie une fabrique distincte : ce jeton voyage dans une URL
/// envoyée par e-mail. L'encodage est donc base64<b>url</b> : le base64 standard contient « + », « / »
/// et « = », que les clients de messagerie et les navigateurs ré-encodent de façon inconstante — un
/// « + » devenu espace suffit à rendre le lien invalide chez une partie des utilisateurs seulement,
/// panne particulièrement pénible à diagnostiquer.
///
/// Un SHA-256 nu suffit (contrairement à un mot de passe) : l'entrée est déjà à haute entropie, une
/// attaque par dictionnaire n'a aucune prise.
/// </summary>
public static class PasswordResetTokenFactory
{
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Condensat stocké. Encodé en base64 STANDARD, comme les refresh tokens : il ne quitte jamais la
    /// base, la contrainte d'URL ne s'applique donc pas ici.
    /// </summary>
    public static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
