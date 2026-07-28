using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Tentative de paiement d'un ABONNEMENT (ticket JGK-I05, docs/Volume_3_DDS.md §5.8) — distincte de
/// <see cref="Payment"/> (encaissement de scolarité, JGK-F02). Table TENANT normale (contrairement à
/// <see cref="Subscription"/>/<see cref="School"/>) : c'est le Directeur lui-même, avec son propre
/// SchoolId de session, qui initie ce paiement — aucune fonction SECURITY DEFINER n'est nécessaire ici,
/// un INSERT EF classique satisfait la policy RLS.
///
/// Créée avec <see cref="Status"/> = Initiated dans CE ticket (JGK-I05). Seul le traitement d'un webhook
/// signé (JGK-I06, pas encore livré) peut la faire passer à Confirmed ou Failed — AGENTS.md règle #11 :
/// « aucune route accessible au client ne positionne SubscriptionPayments.Status = Confirmed ».
///
/// <see cref="ProviderTransactionRef"/> (le jeton retourné par l'agrégateur à la création) est ce qui
/// permettra au futur webhook de retrouver cette ligne sans ambiguïté (déduplication par unicité).
/// </summary>
public class SubscriptionPayment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SubscriptionId { get; set; }

    /// <summary>Montant facturé, en FCFA. Calculé serveur depuis le plan et la période — jamais fourni par le client.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "XOF";

    public SubscriptionPaymentMethod Method { get; set; }

    public BillingPeriod BillingPeriod { get; set; }

    /// <summary>Nom de l'agrégateur ayant traité ce paiement (ex. "PayDunya", "CinetPay") — texte libre, pas un enum : en ajouter un ne doit jamais exiger de migration.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Référence retournée par l'agrégateur à la création de la facture. NULL uniquement si l'appel amont a échoué avant persistance (cas exceptionnel).</summary>
    public string? ProviderTransactionRef { get; set; }

    public SubscriptionPaymentStatus Status { get; set; } = SubscriptionPaymentStatus.Initiated;

    /// <summary>Copie brute du webhook reçu (JGK-I06), pour audit/réconciliation. Vide tant que ce paiement n'est qu'Initiated.</summary>
    public string? WebhookPayloadRaw { get; set; }

    public DateTimeOffset InitiatedAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }
}
