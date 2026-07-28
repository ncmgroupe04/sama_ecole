using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Convocation d'un parent/tuteur à un entretien avec l'établissement (module Vie Scolaire) —
/// motif libre (discipline, assiduité, résultats…), pas restreinte à la discipline pure : c'est un
/// entretien programmé, distinct d'un <see cref="DisciplineRecord"/> qui, lui, sanctionne un fait déjà
/// constaté.
/// </summary>
public class ParentSummons : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTimeOffset ScheduledAt { get; set; }
    public string Reason { get; set; } = null!;
}
