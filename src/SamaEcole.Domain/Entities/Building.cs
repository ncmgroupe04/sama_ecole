using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Bâtiment physique de l'établissement (module Infrastructures). Regroupe des <see cref="Room"/> —
/// concept INDÉPENDANT de <see cref="Classroom"/> (la classe pédagogique) : un Bâtiment/une Salle
/// décrit un local physique, une Classe décrit un groupe d'élèves qui peut occuper des salles
/// différentes selon l'emploi du temps. Aucun lien entre les deux dans cette première version.
/// </summary>
public class Building : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public ICollection<Room> Rooms { get; set; } = [];
}
