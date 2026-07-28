namespace SamaEcole.Web.RateLimiting;

/// <summary>
/// Noms des politiques de limitation de débit du parcours d'inscription self-service (Module I),
/// partagés entre leur enregistrement (Program.cs) et les attributs [EnableRateLimiting] des
/// contrôleurs : une chaîne recopiée à deux endroits finirait par diverger.
/// </summary>
public static class RegistrationRateLimiting
{
    /// <summary>Ticket JGK-I01 — POST /registration-requests (docs/Volume_7_Security.md §Paiements).</summary>
    public const string PolicyName = "registration";

    /// <summary>
    /// Ticket JGK-I02 — GET /registration-requests/{trackingReference}/status. Non exigée par le
    /// cahier des charges, mais la ressource reste accessible SANS authentification par un simple
    /// token dans l'URL (la référence) : sans limite, elle inviterait à un essai en masse de
    /// références pour en découvrir de valides (nom d'école, statut). Plafond plus généreux que la
    /// soumission : un Directeur légitime revient régulièrement consulter l'avancement de son dossier.
    /// </summary>
    public const string StatusPolicyName = "registration-status";
}
