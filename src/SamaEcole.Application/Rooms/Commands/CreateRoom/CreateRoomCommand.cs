using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Rooms.Commands.CreateRoom;

/// <summary>
/// POST /api/v1/rooms — module Infrastructures. Le SchoolId n'est PAS ici : il est lu dans le JWT via
/// ITenantProvider (AGENTS.md règle #10). <see cref="BuildingId"/> doit désigner un bâtiment DE CETTE
/// ÉCOLE — vérifié par le Handler (même principe que CreateGradeCommand vis-à-vis de l'élève/matière).
/// </summary>
public record CreateRoomCommand : IRequest<RoomResult>
{
    public required string Name { get; init; }
    public int Capacity { get; init; }
    public RoomType Type { get; init; } = RoomType.SalleDeClasse;
    public Guid BuildingId { get; init; }
}

public record RoomResult(Guid Id, string Name, int Capacity, RoomType Type, Guid BuildingId, uint RowVersion);
