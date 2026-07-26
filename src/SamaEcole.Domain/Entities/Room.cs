using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Salle physique d'un <see cref="Building"/> (module Infrastructures). <see cref="Capacity"/> est le
/// nombre de places PHYSIQUES de la salle — à ne pas confondre avec <see cref="Classroom.Capacity"/>,
/// l'effectif pédagogique maximal d'une classe : deux notions distinctes, volontairement non reliées
/// dans cette version.
/// </summary>
public class Room : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    public int Capacity { get; set; }

    public RoomType Type { get; set; } = RoomType.SalleDeClasse;

    public Guid BuildingId { get; set; }

    public Building? Building { get; set; }
}
