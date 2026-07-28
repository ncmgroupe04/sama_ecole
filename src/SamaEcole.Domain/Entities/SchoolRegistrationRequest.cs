using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Demande d'inscription self-service d'un établissement (ticket JGK-I01, docs/Volume_1_Cahier_des_Charges.md
/// §11.5, docs/Volume_3_DDS.md §5.7). Soumise SANS authentification via le formulaire public, elle PRÉCÈDE
/// la création de l'école : à ce stade aucun <see cref="School"/>, aucun <see cref="User"/>, aucun
/// <see cref="Subscription"/> n'existe.
///
/// Table PLATEFORME, hors périmètre RLS/tenant (docs/Volume_3_DDS.md §2.3) : gérée par le Super Admin,
/// jamais filtrée par SchoolId. N'implémente donc pas ITenantEntity — comme <see cref="School"/> et
/// <see cref="Subscription"/>.
///
/// <see cref="DirectorPasswordHash"/> est haché DÈS la soumission (jamais stocké ni journalisé en clair,
/// AGENTS.md — docs/Volume_7_Security.md §Paiements). Le mot de passe choisi par le Directeur ne réapparaît
/// nulle part : l'e-mail de confirmation ne transmet QUE la référence de suivi.
///
/// Aucune ligne de cette table ne devient jamais une ligne de <see cref="School"/> par simple mise à jour de
/// statut : l'approbation (ticket JGK-I03) CRÉE une nouvelle école dans une transaction dédiée et renseigne
/// <see cref="CreatedSchoolId"/> ; la demande, elle, reste un historique immuable de la candidature.
/// </summary>
public class SchoolRegistrationRequest : AuditableEntity
{
    /// <summary>
    /// Référence communiquée au demandeur pour suivre sa demande SANS authentification (ticket JGK-I02).
    /// Unique sur toute la plateforme (aucune école n'existe encore pour la cloisonner) — l'unicité est
    /// tenue par la BASE (index unique), pas seulement par le code.
    /// </summary>
    public required string TrackingReference { get; set; }

    public required string DirectorFullName { get; set; }
    public required string DirectorEmail { get; set; }
    public required string DirectorPhone { get; set; }

    /// <summary>Haché dès la soumission (Identity), jamais en clair. Voir remarque de classe.</summary>
    public required string DirectorPasswordHash { get; set; }

    public required string SchoolName { get; set; }
    public string? SchoolAddress { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }

    /// <summary>Effectif approximatif déclaré par le Directeur (aide le Super Admin à qualifier la demande).</summary>
    public int? EstimatedStudentCount { get; set; }

    /// <summary>
    /// Plan souhaité — simple préférence déclarée à ce stade, aucun paiement n'est demandé avant validation
    /// (docs/Volume_1_Cahier_des_Charges.md §11.5). Enum plutôt que FK : le MVP ne matérialise pas de table
    /// SubscriptionPlans, l'abonnement porte lui-même son plan en enum (voir <see cref="Subscription.Plan"/>).
    /// </summary>
    public SubscriptionPlan RequestedPlan { get; set; }

    public RegistrationRequestStatus Status { get; set; } = RegistrationRequestStatus.Pending;

    /// <summary>Motif obligatoire (applicativement) lorsque <see cref="Status"/> passe à Rejected (ticket JGK-I03).</summary>
    public string? RejectionReason { get; set; }

    /// <summary>Super Admin ayant tranché la demande. NULL tant que <see cref="Status"/> = Pending.</summary>
    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>École créée par l'approbation. NULL tant que la demande n'a pas été approuvée effectivement.</summary>
    public Guid? CreatedSchoolId { get; set; }
}
