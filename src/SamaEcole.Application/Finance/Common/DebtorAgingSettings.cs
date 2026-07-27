namespace SamaEcole.Application.Finance.Common;

/// <summary>
/// Réglages de DebtorAgingHostedService (Étape 5 — recouvrement semi-automatique). Section
/// « Finance:DebtorAging » (voir .env.example et AddInfrastructure), même idiome que SmsQueueSettings.
/// </summary>
public class DebtorAgingSettings
{
    /// <summary>
    /// Coupé par défaut dans les tests fonctionnels (Finance__DebtorAging__Enabled=false, voir
    /// AuthApiFactory) — même raison que SmsQueueSettings.Enabled : un lot brouillon créé pendant
    /// l'exécution d'un test rendrait son résultat non déterministe.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cadence entre deux calculs. 24h suffit largement (un retard ne varie pas d'heure en heure) —
    /// aucun alignement sur minuit local : un lot calculé à un autre moment de la journée n'a aucune
    /// conséquence produit, et l'alignement horaire ajouterait une gestion de fuseau sans bénéfice.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
}
