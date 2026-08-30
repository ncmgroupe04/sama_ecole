namespace SamaEcole.Application.StateIntegration;

/// <summary>
/// Réglages du module Intégration étatique, liés depuis <c>appsettings</c> (section
/// <c>StateIntegration</c>). Volume 9 §configuration.
///
/// AUCUN SECRET ICI. La clé d'API du SIMEN, le jour où elle existera, vivra dans la configuration
/// protégée de l'hôte comme les identifiants de l'agrégateur de paiement — jamais dans un objet de
/// réglages sérialisable qu'un écran d'administration pourrait un jour exposer.
/// </summary>
public class StateIntegrationSettings
{
    /// <summary>
    /// Base publique des URL de vérification imprimées en QR code sur le certificat de mutation
    /// (« https://app.sama-ecole.sn » → « …/verifier/mutation/{code} »).
    ///
    /// Elle est CONFIGURÉE et non déduite de la requête entrante : un certificat est un papier qui
    /// circule des mois. Construire son URL depuis l'en-tête <c>Host</c> de la requête ferait imprimer
    /// l'adresse interne du conteneur derrière un proxy, ou pire, une adresse fournie par un client
    /// malveillant — le QR d'une pièce officielle pointerait alors où il aurait décidé.
    /// </summary>
    public string PublicBaseUrl { get; init; } = "https://localhost:5001";

    /// <summary>
    /// Point d'accès du relais SIMEN. VIDE par défaut, et c'est l'état normal : aucune API publique
    /// n'existe à ce jour. Tant qu'il est vide, <c>ISimenBridgeService.IsConfigured</c> vaut faux et
    /// l'interface n'affiche pas l'action de transmission.
    /// </summary>
    public string? SimenApiBaseUrl { get; init; }
}
