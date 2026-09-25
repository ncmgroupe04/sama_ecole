using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Dispense d'un élève pour une matière OBLIGATOIRE, pour UNE année scolaire, avec un MOTIF. Calquée sur
/// <see cref="StudentSubjectEnrollment"/> (élève + année) : rattachée à l'élève et à l'année, pas à l'inscription.
/// « Aucune ligne » = « l'élève suit la matière » : rien ne change tant que personne n'a dispensé personne.
///
/// La matière dispensée sort des moyennes et de la saisie (SubjectFollowScope) ; le bulletin la garde, marquée
/// « Dispensé(e) ». Le <see cref="Reason"/> peut être médical : il n'est jamais imprimé ni journalisé.
///
/// Suppression logique uniquement (règle #6) : « refaire la liste » retire les lignes en trop et la clé redevient
/// libre grâce à l'index unique partiel. Pas de verrou xmin : l'écriture est un remplacement idempotent.
/// </summary>
public class StudentSubjectExemption : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public Guid SubjectId { get; set; }

    public Guid SchoolYearId { get; set; }

    /// <summary>Motif obligatoire (200 caractères au plus). Donnée sensible.</summary>
    public string Reason { get; set; } = string.Empty;
}
