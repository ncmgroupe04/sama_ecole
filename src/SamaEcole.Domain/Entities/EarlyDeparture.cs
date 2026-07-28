using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Sortie anticipée d'un élève en cours de journée (module Surveillance) — pendant symétrique de
/// <see cref="LateArrival"/> : au lieu d'un retard à l'entrée, une sortie avant l'heure normale.
/// <see cref="PickedUpBy"/> trace qui est venu chercher l'élève (exigence de sécurité courante des
/// établissements sénégalais) — optionnel : un élève peut aussi sortir seul avec autorisation écrite.
/// </summary>
public class EarlyDeparture : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTime Date { get; set; }
    public TimeOnly DepartureTime { get; set; }
    public string Reason { get; set; } = null!;
    public string? PickedUpBy { get; set; }
}
