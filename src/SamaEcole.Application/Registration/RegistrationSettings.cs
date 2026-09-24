namespace SamaEcole.Application.Registration;

/// <summary>
/// Réglages du flux d'inscription self-service (section "Registration" de la configuration, tickets
/// JGK-I01/I03). Même idiome que <see cref="SamaEcole.Application.Auth.AuthSettings"/> et
/// <see cref="SamaEcole.Application.StateIntegration.StateIntegrationSettings"/> : un objet de réglages
/// propre à ce module dans Application, lié depuis Infrastructure — jamais de lecture directe
/// d'IConfiguration ni de dépendance vers les options Infrastructure (SmtpOptions).
/// </summary>
public class RegistrationSettings
{
    /// <summary>
    /// Racine publique de l'application, utilisée pour construire les liens envoyés par e-mail dans ce
    /// flux : le lien de connexion transmis au Directeur après approbation, et le lien vers
    /// <c>/admin/inscriptions</c> dans l'alerte adressée au Super Admin. Doit être l'URL vue par
    /// l'UTILISATEUR (pas l'adresse interne du conteneur) — même remarque que AuthSettings.PublicBaseUrl.
    /// </summary>
    public string PublicBaseUrl { get; init; } = "https://localhost:5001";

    /// <summary>
    /// Adresse recevant une alerte à chaque nouvelle demande d'inscription soumise. VIDE par défaut :
    /// aucune alerte n'est alors envoyée, le Super Admin découvre les demandes via le tableau de bord
    /// (comportement d'origine, docs/Volume_1_Cahier_des_Charges.md §11.5 — l'alerte est un confort
    /// additionnel, pas un mécanisme dont la Revue par le Super Admin dépend).
    /// </summary>
    public string? AdminNotificationEmail { get; init; }
}
