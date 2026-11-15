namespace SamaEcole.Web.Security;

public enum TurnstileVerdict
{
    /// <summary>Cloudflare confirme que le jeton est valide (humain probable).</summary>
    Valid,

    /// <summary>Jeton absent, expiré, déjà utilisé ou forgé : la demande est refusée.</summary>
    Invalid,

    /// <summary>Cloudflare n'a pas pu répondre (réseau, délai) : on échoue FERMÉ, l'utilisateur réessaie.</summary>
    Unavailable
}

/// <summary>Vérifie, côté serveur, le jeton produit par le widget Turnstile du navigateur.</summary>
public interface ITurnstileVerifier
{
    Task<TurnstileVerdict> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}
