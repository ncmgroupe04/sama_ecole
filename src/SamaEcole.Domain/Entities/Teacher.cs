using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Enseignant d'un établissement (ticket JGK-D03). Même règle de matricule que
/// <see cref="Student"/> : généré par le Handler, dans la transaction d'enregistrement,
/// jamais à l'ouverture du formulaire (AGENTS.md règle #3).
///
/// Dossier RH distinct du compte de connexion `User` (rôle <c>Enseignant</c>) : aucun lien n'existe
/// aujourd'hui entre les deux, un enseignant peut avoir une fiche sans jamais se connecter à la
/// plateforme (docs/Volume_7_Security.md « Enseignants » : gérée par Directeur/Secrétariat).
/// </summary>
public class Teacher : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Matricule { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }

    /// <summary>Obligatoire à la création (formulaire Enseignant) — voir <see cref="Student.BirthDate"/>.</summary>
    public required DateOnly BirthDate { get; set; }

    public string? BirthPlace { get; set; }

    /// <summary>URL de la photo d'identité — même contrat que <see cref="Student.PhotoUrl"/>.</summary>
    public string? PhotoUrl { get; set; }

    /// <summary>Photo téléversée (feature B) — même contrat que <see cref="Student.PhotoData"/>.</summary>
    public byte[]? PhotoData { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;

    /// <summary>
    /// Compte de connexion (rôle Enseignant) rattaché à cette fiche RH (ticket JGK-D06). Nullable : une
    /// fiche peut exister sans compte (enseignant qui n'utilise pas la plateforme). C'est ce lien qui
    /// permet de savoir QUEL enseignant est connecté pour borner la saisie de l'appel à ses classes et
    /// matières assignées — sans lui, le JWT ne porte que l'identifiant du compte, pas la fiche.
    /// </summary>
    public Guid? UserId { get; set; }
}
