namespace SamaEcole.Domain.Enums;

/// <summary>
/// Cycle de vie d'un échéancier personnalisé (Étape 5 — recouvrement). <c>Cancelled</c> ne supprime
/// jamais la ligne (règle #6) : un nouvel échéancier accepté sur la même inscription bascule l'ancien
/// à ce statut plutôt que de l'écraser, pour garder la trace de ce qui a été renégocié.
/// </summary>
public enum FeeInstallmentPlanStatus
{
    Active,
    Cancelled
}

/// <summary>
/// Cycle de vie d'un lot de relance de débiteurs (Étape 5 — recouvrement semi-automatique).
/// <c>Draft</c> : généré chaque nuit par le calcul d'ancienneté, jamais envoyé de lui-même (§13.7 du
/// cahier des charges : aucun envoi de masse automatique). <c>Sent</c> : un Directeur/Finance a
/// déclenché l'envoi à la main. <c>Dismissed</c> : écarté sans envoi (ex. classe déjà relancée
/// autrement) — jamais supprimé physiquement (règle #6).
/// </summary>
public enum DebtorReminderBatchStatus
{
    Draft,
    Sent,
    Dismissed
}
