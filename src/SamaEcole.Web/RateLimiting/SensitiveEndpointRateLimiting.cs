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

    /// <summary>
    /// GET /public/schools et /public/schools/{id} — annuaire B2C. Routes ANONYMES et destinées à être
    /// parcourues : la limite est donc large (elle ne doit gêner aucun visiteur qui feuillette les
    /// pages), mais elle existe, car un endpoint public non borné est une invitation au moissonnage
    /// systématique de l'annuaire et à la saturation du serveur depuis une seule source.
    /// </summary>
    public const string PublicDirectoryPolicyName = "public-directory";

    /// <summary>
    /// POST /webhooks/payments/{provider} et POST /webhooks/sms/{provider} — accusés serveur-à-serveur
    /// de l'agrégateur de paiement (PayDunya/CinetPay) et du fournisseur SMS. Routes [AllowAnonymous]
    /// par nature (l'émetteur n'a pas de JWT) : la vraie garde est la signature HMAC vérifiée dans le
    /// Handler. Cette limite, par IP, ajoute un simple plafond anti-flood sur deux endpoints publics
    /// non authentifiés. Volontairement GÉNÉREUSE : un agrégateur rejoue légitimement (retry réseau) et
    /// peut émettre des rafales d'accusés (batch de DLR) — la limite casse l'abus de masse, pas le
    /// trafic normal. Le corps non signé est rejeté avant toute recherche en base, de toute façon.
    /// </summary>
    public const string WebhookInboundPolicyName = "webhooks-inbound";
}
