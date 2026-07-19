namespace SamaEcole.Infrastructure.Payments;

/// <summary>
/// Section "PayDunya" de la configuration (appsettings.json, .env.example) — ticket JGK-I05.
///
/// Les 4 clés sont VIDES par défaut et doivent être fournies par l'environnement (jamais committées,
/// AGENTS.md). Sans elles, PayDunyaPaymentService lève une PaymentProviderException explicite au
/// premier appel plutôt que d'envoyer une requête vouée à un 401 — voir sa validation au démarrage.
/// </summary>
public class PayDunyaOptions
{
    public const string SectionName = "PayDunya";

    public string MasterKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;

    /// <summary>API de création de facture (checkout-invoice/create) — inchangée entre bac à sable et production chez PayDunya, seules les clés distinguent les deux modes.</summary>
    public string ApiBaseUrl { get; set; } = "https://app.paydunya.com/api/v1";

    /// <summary>Base du guichet de paiement hébergé : l'URL de redirection est {CheckoutBaseUrl}/{token}.</summary>
    public string CheckoutBaseUrl { get; set; } = "https://paydunya.com/checkout/invoice";

    /// <summary>
    /// Origine PUBLIQUE de Sama Ecole, utilisée pour construire les URLs de retour/annulation/callback
    /// transmises à PayDunya (l'API et les pages Razor partagent le même hôte dans ce projet).
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Fausse ("REMPLACER", le sentinel de .env.example) ou absente : dans les deux cas, aucun compte
    /// marchand n'a été renseigné. Centralisé ici pour que PayDunyaPaymentService (garde d'appel) ET
    /// DependencyInjection (choix entre PayDunyaPaymentService et DevPaymentService, Development
    /// uniquement) partagent EXACTEMENT la même définition de "non configuré".
    /// </summary>
    public bool IsConfigured =>
        IsKeyConfigured(MasterKey) && IsKeyConfigured(PrivateKey)
        && IsKeyConfigured(PublicKey) && IsKeyConfigured(Token);

    private static bool IsKeyConfigured(string key) =>
        !string.IsNullOrWhiteSpace(key) && key != "REMPLACER";
}
