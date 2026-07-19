namespace SamaEcole.Web.RateLimiting;

/// <summary>
/// Noms des politiques de limitation de débit des endpoints authentifiés sensibles (force brute sur
/// le login, génération PDF coûteuse), partagés entre leur enregistrement (Program.cs) et les
/// attributs [EnableRateLimiting] des contrôleurs — voir <see cref="RegistrationRateLimiting"/> pour
/// le même principe côté inscription publique.
/// </summary>
public static class SensitiveEndpointRateLimiting
{
    /// <summary>
    /// POST /auth/login. Le verrouillage de compte (Auth:MaxFailedAttempts/LockoutMinutes) protège
    /// déjà un compte donné contre la force brute — cette limite, partitionnée par IP, protège contre
    /// le credential stuffing distribué sur de nombreux comptes depuis une même source.
    /// </summary>
    public const string LoginPolicyName = "auth-login";

    /// <summary>
    /// POST /report-cards/generate. Chaque appel régénère le PDF à la volée (aucun cache, aucune
    /// entité ReportCard persistée) : un flot de requêtes depuis un même compte compromis ou script
    /// saturerait le serveur de rendu QuestPDF sans que le verrouillage de compte n'intervienne
    /// (l'utilisateur est déjà authentifié). Partitionné par utilisateur, pas par IP : plusieurs
    /// enseignants derrière un même établissement/proxy ne doivent pas se pénaliser entre eux.
    /// </summary>
    public const string ReportCardGenerationPolicyName = "report-card-generation";
}
