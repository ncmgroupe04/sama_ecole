namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Réglages de SubscriptionLifecycleHostedService (ticket JGK-B03 — alertes d'expiration et passage
/// automatique en lecture seule). Section « Subscriptions:Lifecycle » (voir .env.example et
/// AddInfrastructure), même idiome que DebtorAgingSettings/SmsQueueSettings.
/// </summary>
public class SubscriptionLifecycleSettings
{
    /// <summary>
    /// Coupé par défaut dans les tests fonctionnels (Subscriptions__Lifecycle__Enabled=false, voir
    /// AuthApiFactory) — même raison que DebtorAgingSettings.Enabled : un abonnement basculé en
    /// ReadOnly pendant l'exécution d'un test rendrait son résultat non déterministe. Le worker est
    /// testé pour lui-même via ISubscriptionAdminStore.ExpireOverdueSubscriptionsAsync.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cadence entre deux tours. 24h suffit : une échéance ne varie pas d'heure en heure.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Jours avant échéance déclenchant un rappel (Volume_1_Cahier_des_Charges.md §11 : « alertes
    /// automatiques 30/15/7 jours avant expiration »). Une seule liste, partagée par le job — chaque
    /// abonnement Actif n'y correspond qu'un jour précis par tour (ExpiresAt - AsOf), jamais deux.
    /// </summary>
    public IReadOnlyList<int> ReminderDaysBeforeExpiry { get; set; } = [30, 15, 7];
}
