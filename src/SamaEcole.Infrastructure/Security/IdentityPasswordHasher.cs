using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace SamaEcole.Infrastructure.Security;

/// <summary>
/// Hachage des mots de passe par ASP.NET Core Identity (PBKDF2-HMAC-SHA256, sel aléatoire par mot de
/// passe, itérations à jour) — docs/Volume_7_Security.md §6, ticket JGK-A04.
///
/// On réutilise le hacheur d'Identity SANS adopter tout son modèle de stockage : l'entité User du
/// projet porte déjà l'audit, le soft delete, le SchoolId nullable et la RLS. Basculer sur
/// IdentityDbContext aurait imposé de refaire le schéma d'A02/A03 pour un bénéfice nul ici.
///
/// La vérification renvoie aussi SuccessRehashNeeded quand le facteur de coût d'Identity a évolué :
/// on l'accepte comme un succès. Le re-hachage transparent viendra avec le changement de mot de passe.
/// </summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    // Le hacheur d'Identity prend l'utilisateur en paramètre mais ne s'en sert pas (aucun sel dérivé
    // de l'identité) : une instance neutre suffit et évite de faire circuler l'entité jusqu'ici.
    private static readonly User Placeholder = new()
    {
        Email = string.Empty,
        PasswordHash = string.Empty,
        FullName = string.Empty
    };

    public string Hash(string password) => _hasher.HashPassword(Placeholder, password);

    public bool Verify(string hash, string password)
    {
        try
        {
            var result = _hasher.VerifyHashedPassword(Placeholder, hash, password);

            return result is PasswordVerificationResult.Success
                          or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // Identity décode le hash en base64 et LÈVE si la chaîne est malformée (hash corrompu,
            // tronqué, ou compte importé d'un autre système). Un tel compte doit échouer à
            // s'authentifier — pas faire tomber /auth/login en erreur 500.
            return false;
        }
    }
}
