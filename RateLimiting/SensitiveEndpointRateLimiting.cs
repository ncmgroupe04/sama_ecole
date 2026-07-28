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

    /// <summary>
    /// POST /auth/forgot-password et /auth/reset-password. Routes ANONYMES, donc sans verrouillage de
    /// compte pour les protéger : la limite par IP est ici la seule barrière. Elle borne deux abus
    /// distincts — le balayage d'adresses pour énumérer les comptes (que la réponse uniforme rend déjà
    /// muet, mais qui coûterait des requêtes et des e-mails), et le bombardement d'une boîte mail par
    /// demandes répétées.
    ///
    /// Volontairement plus stricte que le login (5 / 15 min contre 10 / 5 min) : réinitialiser son mot
    /// de passe est un geste rare, une limite basse ne gêne aucun usage légitime.
    /// </summary>
    public const string PasswordResetPolicyName = "auth-password-reset";
}
