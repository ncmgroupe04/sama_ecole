using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Table plateforme rattachée à une école mais hors périmètre RLS/tenant (docs/Volume_3_DDS.md §2.3,
/// docs/ERD.md note) : gérée par le Super Admin, jamais filtrée par SchoolId dans les requêtes des
/// utilisateurs de l'école. N'implémente donc pas ITenantEntity.
/// </summary>
public class Subscription : AuditableEntity
{
    public Guid SchoolId { get; set; }
    public SubscriptionPlan Plan { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
}