using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal d'audit centralisé (ticket JGK-H01, docs/Volume_7_Security.md §7) : paiements, changements
/// de statut/mot de passe utilisateur, création de compte, actions Super Admin, impressions/exports
/// de reçus. Table tenant standard (SchoolId non nul, contrairement à Users : docs/Volume_3_DDS.md
/// §4.6 ne place PAS AuditLogs parmi les entités hors périmètre SchoolId de §2.3/§4.1 — seule
/// PlatformAuditLogs, une table distincte hors MVP, l'est).
///
/// APPEND-ONLY IMPOSÉ PAR LA BASE (comme UserStatusHistory) : « consultable mais jamais modifiable,
/// y compris par un administrateur » — voir la migration pour les GRANT (SELECT, INSERT uniquement).
/// </summary>
public class AuditLog : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>
    /// Acteur de l'action — toujours déterminable pour ce qui est journalisé ici : voir
    /// AuditLoggingBehavior (une tentative de connexion sur un e-mail inconnu, sans école ni
    /// utilisateur réel à qui l'imputer, n'est délibérément pas journalisée dans cette table tenant).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>Domaine fonctionnel, dérivé automatiquement de l'espace de noms de la commande (ex. "Finance", "Users").</summary>
    public required string Module { get; set; }

    /// <summary>Nom de l'action, dérivé automatiquement du nom de la commande/requête (ex. "RecordPayment").</summary>
    public required string Action { get; set; }

    public bool Success { get; set; }

    /// <summary>Motif d'échec — le message déjà renvoyé au client, jamais une donnée nouvelle (aucun risque de fuite).</summary>
    public string? FailureReason { get; set; }

    public string? IpAddress { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
