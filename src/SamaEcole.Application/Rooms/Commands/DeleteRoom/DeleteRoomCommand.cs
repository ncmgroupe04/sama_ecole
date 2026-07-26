using MediatR;

namespace SamaEcole.Application.Rooms.Commands.DeleteRoom;

/// <summary>
/// DELETE /api/v1/rooms/{id} — archive (soft delete) une salle créée par erreur. Toujours un soft
/// delete (AGENTS.md règle #6). Aucune règle métier de type « salle encore utilisée » dans cette
/// version : rien ne référence Room pour l'instant (contrairement à Classroom/Student).
/// <paramref name="RowVersion"/> : verrouillage optimiste (règle #5).
/// </summary>
public record DeleteRoomCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
