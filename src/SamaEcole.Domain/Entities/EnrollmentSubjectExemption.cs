using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Dispense d'un élève pour une matière, portée par son INSCRIPTION (donc par l'année scolaire). Deux
/// sortes, déduites de <see cref="Subject.IsOptional"/> et de <see cref="Reason"/> — pas de colonne « type » :
/// — une OPTION non suivie : matière optionnelle, sans motif ;
/// — la dispense d'une matière OBLIGATOIRE : matière obligatoire, AVEC motif (imposé par la commande).
///
/// « Aucune ligne » signifie « l'élève suit toutes les matières » : c'est ce qui garantit que rien ne change
/// tant que personne n'a enregistré de choix. Une ligne est ACTIVE quand la matière est encore optionnelle OU
/// quand elle porte un motif (SubjectExemptions) : repasser une matière en « obligatoire » rend ses lignes
/// d'option, sans motif, inertes.
///
/// Suppression logique uniquement (règle #6) : « refaire son choix » retire les lignes en trop et la clé
/// redevient libre grâce à l'index unique partiel. Pas de verrou xmin : ce n'est pas une donnée sensible
/// au sens de la règle #5, et l'écriture est un remplacement idempotent.
/// </summary>
public class EnrollmentSubjectExemption : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid EnrollmentId { get; set; }

    public Guid SubjectId { get; set; }

    /// <summary>
    /// Motif de la dispense (200 caractères au plus) : obligatoire pour une matière OBLIGATOIRE, vide pour une
    /// option non suivie. Peut être médical : il n'est jamais imprimé sur un document.
    /// </summary>
    public string? Reason { get; set; }
}
