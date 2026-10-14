namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Compte marchand Wave de la plateforme (section « Wave » de la configuration, voir AddInfrastructure) —
/// moyen de paiement d'abonnement <c>SubscriptionPaymentMethod.WaveMerchant</c>. Le lien est PUBLIC (c'est
/// celui du QR code marchand) : ce n'est pas un secret, il peut donc vivre dans appsettings.json.
/// Un lien marchand statique ne porte ni montant ni référence et Wave n'émet aucun webhook : le paiement
/// ne peut donc être rapproché et confirmé que par le Super Admin (AGENTS.md règle #11).
/// </summary>
public class WaveMerchantSettings
{
    /// <summary>Valeur de SubscriptionPayment.Provider pour ce moyen de paiement (texte libre en base, jamais un enum).</summary>
    public const string ProviderName = "WaveMerchant";

    /// <summary>Préfixe de la référence de rapprochement (SubscriptionPayment.ProviderTransactionRef).</summary>
    public const string ReferencePrefix = "WAVE-";

    /// <summary>Lien marchand Wave (ex. https://pay.wave.com/m/M_xxx/c/sn/). Vide : le moyen de paiement est indisponible.</summary>
    public string PaymentUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        Uri.TryCreate(PaymentUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
