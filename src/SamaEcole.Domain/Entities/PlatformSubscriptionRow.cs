namespace SamaEcole.Domain.Entities;

/// <summary>
/// Une ligne plateforme, lecture seule, adossée à la vue PostgreSQL `v_platform_subscriptions`
/// (WITH (security_invoker = false), OWNER sama_ecole) : contourne la RLS pour le Super Admin, qui
/// n'a aucun SchoolId propre et ne peut donc jamais lire `subscriptions`/`subscription_payments` par
/// une requête EF Core normale (AGENTS.md règle #2, docs/Volume_7_Security.md §8). Une ligne par
/// école ayant un abonnement, avec son dernier paiement CONFIRMÉ le cas échéant. Entité SANS CLÉ
/// (HasNoKey) : SchoolId ne l'est pas au sens EF, c'est une projection, pas une entité identifiable.
/// </summary>
public class PlatformSubscriptionRow
{
    public Guid SchoolId { get; set; }

    public string SchoolName { get; set; } = string.Empty;

    public string Plan { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateOnly? ExpiresAt { get; set; }

    /// <summary>Montant du dernier paiement CONFIRMÉ (AGENTS.md règle #11) — NULL si l'école n'en a encore aucun.</summary>
    public decimal? LastPaymentAmountXof { get; set; }

    public DateTimeOffset? LastPaymentAt { get; set; }

    public string? LastPaymentBillingPeriod { get; set; }
}
