using SamaEcole.Application.Rooms.Commands.CreateRoom;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Rooms.Commands.UpdateRoom;

/// <summary>
/// PUT /api/v1/rooms/{id} — corrige le nom/la capacité/le type d'une salle déjà créée. Ne permet PAS
/// de changer le bâtiment (BuildingId absent) : déplacer une salle d'un bâtiment à l'autre n'est pas
/// un cas d'usage de cette version — supprimer/recréer reste la voie si besoin.
/// <see cref="RowVersion"/> : verrouillage optimiste (AGENTS.md règle #5).
/// </summary>
public record UpdateRoomCommand(Guid Id, string Name, int Capacity, RoomType Type, uint RowVersion)
    : IRequest<RoomResult>;
