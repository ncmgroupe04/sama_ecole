namespace SamaEcole.Application.Common.Exceptions;

/// <summary>
/// Compte temporairement verrouillé après plusieurs échecs de connexion consécutifs (blocage
/// progressif anti-force-brute, docs/Volume_7_Security.md §2). Contrairement à
/// InvalidCredentialsException, révéler ce statut ne rouvre pas l'énumération de comptes que celle-ci
/// évite : le verrou ne se déclenche qu'après plusieurs échecs déjà commis sur CE compte précis, un
/// attaquant qui le martèle sait donc déjà qu'il vise un compte existant.
/// </summary>
public class AccountLockedException(int retryAfterSeconds)
    : Exception("Compte temporairement verrouillé suite à plusieurs échecs de connexion. Réessayez plus tard.")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
