using MediatR;

namespace SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;

/// <summary>
/// GET /api/v1/buildings — module Infrastructures. Aucun paramètre : les bâtiments de l'école
/// courante, et eux seuls (Global Query Filter + policy RLS, comme GetClassroomsQuery).
///
/// Renvoie la hiérarchie complète (bâtiment + ses salles) en UNE requête : contrairement à Classroom
/// (liste plate groupée par cycle côté client), Bâtiment/Salle est une VRAIE hiérarchie à 2 niveaux
/// que l'écran doit afficher telle quelle.
/// </summary>
public record GetBuildingsWithRoomsQuery : IRequest<IReadOnlyList<BuildingWithRoomsDto>>;

/// <summary><see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateBuildingCommand/DeleteBuildingCommand.</summary>
public record BuildingWithRoomsDto(
    Guid Id,
    string Name,
    string? Description,
    uint RowVersion,
    IReadOnlyList<RoomDto> Rooms);

/// <summary><see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateRoomCommand/DeleteRoomCommand.</summary>
public record RoomDto(
    Guid Id,
    string Name,
    int Capacity,
    string Type,
    Guid BuildingId,
    uint RowVersion);
