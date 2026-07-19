namespace SamaEcole.Domain.Entities;

/// <summary>
/// Agrégat plateforme, lecture seule, adossé à la vue PostgreSQL `v_platform_dashboard_stats`
/// (WITH (security_invoker = false), OWNER sama_ecole) : contourne la RLS pour le Super Admin, qui
/// n'a aucun SchoolId propre et ne peut donc jamais lire une table tenant par une requête EF Core
/// normale (AGENTS.md règle #2, docs/Volume_7_Security.md §8). Entité SANS CLÉ (HasNoKey) — ce n'est
/// pas une ligne identifiable, seulement l'unique ligne d'agrégats de la vue.
/// </summary>
public class PlatformDashboardStats
{
    public int TotalSchools { get; set; }

    public int TotalUsers { get; set; }

    /// <summary>Somme des paiements d'abonnement CONFIRMÉS uniquement (AGENTS.md règle #11) — un
    /// paiement Initiated ou Failed n'est jamais un revenu réel.</summary>
    public decimal TotalRevenue { get; set; }

    public int ActiveSubscriptions { get; set; }
}
