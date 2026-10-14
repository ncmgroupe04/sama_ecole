using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Ticket JGK-I06 — porte étroite d'accès à `subscription_payments`/`subscriptions` pour l'acteur
/// WEBHOOK (anonyme, aucun SchoolId de session). Même raison que ISchoolProvisioningStore/IAuditLogStore :
/// les deux tables sont sous policy RLS, et un acteur sans tenant ne satisferait le USING d'aucune ligne.
/// L'implémentation passe par des fonctions PostgreSQL SECURITY DEFINER.
/// </summary>
public interface ISubscriptionPaymentStore
{
    /// <summary>
    /// Lecture SEULE, avant tout appel à l'agrégateur : évite un appel réseau coûteux pour une référence
    /// inconnue. Renvoie null si <paramref name="internalPaymentId"/> ne correspond à AUCUNE ligne.
    /// </summary>
    Task<SubscriptionPaymentLookup?> FindAsync(Guid internalPaymentId, CancellationToken cancellationToken);

    /// <summary>
    /// Transition ATOMIQUE et gardée : ne modifie RIEN si la ligne n'est plus au statut Initiated au
    /// moment de l'écriture (rejeu concurrent d'un webhook, ticket JGK-I06 — critère d'idempotence).
    /// Si <paramref name="finalStatus"/> = Confirmed, active aussi l'abonnement (Status = Active,
    /// ExpiresAt prolongée depuis la date la plus tardive entre aujourd'hui et l'expiration actuelle)
    /// DANS LA MÊME transaction côté serveur PostgreSQL. Renvoie null si la transition n'a pas eu lieu
    /// (déjà traité par un appel concurrent) — c'est le signal d'idempotence pour le Handler.
    /// </summary>
    Task<SubscriptionPaymentConfirmation?> ConfirmAsync(
        Guid internalPaymentId,
        SubscriptionPaymentStatus finalStatus,
        string webhookPayloadJson,
        CancellationToken cancellationToken);

    /// <summary>
    /// Console Super Admin — paiements d'abonnement par Wave marchand (Provider = WaveMerchant) encore au
    /// statut Initiated, toutes écoles confondues, du plus ancien au plus récent. Lecture inter-écoles : passe
    /// par la fonction SECURITY DEFINER list_pending_wave_payments, jamais une requête EF (RLS).
    /// </summary>
    Task<IReadOnlyList<PendingWavePayment>> ListPendingWavePaymentsAsync(CancellationToken cancellationToken);
}

/// <summary>Une demande de paiement Wave en attente de validation manuelle par le Super Admin.</summary>
public record PendingWavePayment(
    Guid PaymentId,
    Guid SchoolId,
    string SchoolName,
    string? DirectorFullName,
    string? DirectorEmail,
    string? DirectorPhone,
    decimal Amount,
    BillingPeriod BillingPeriod,
    string Reference,
    DateTimeOffset InitiatedAt);

public record SubscriptionPaymentLookup(
    string ProviderTransactionRef, decimal Amount, SubscriptionPaymentStatus Status);

public record SubscriptionPaymentConfirmation(
    Guid SchoolId,
    string SchoolName,
    Guid DirectorUserId,
    string DirectorEmail,
    string DirectorFullName,
    DateOnly? NewExpiresAt);
