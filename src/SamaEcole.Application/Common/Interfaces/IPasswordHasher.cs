namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Abstraction du hachage de mot de passe. Implémentée dans SamaEcole.Infrastructure par
/// ASP.NET Core Identity (PBKDF2 — docs/Volume_7_Security.md §6) : Application ne référence
/// jamais Identity directement.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Comparaison en temps constant, déléguée à Identity.</summary>
    bool Verify(string hash, string password);
}