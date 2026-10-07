using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding;

/// <summary>Résultat d'écriture d'un pavillon. Le nom du surveillant est celui du compte lié s'il existe.</summary>
public record DormitoryDto(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, string? Notes, uint RowVersion);

/// <summary>Ligne de liste : capacités DÉRIVÉES des lits (spec N1), jamais saisies.</summary>
public record DormitorySummaryDto(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, int RoomCount, int Capacity, int OccupiedBeds, int MaintenanceBeds,
    decimal OccupancyRate, uint RowVersion);

public record BedDto(
    Guid Id, Guid DormitoryRoomId, int BedNumber, BedStatus Status,
    Guid? OccupantBoarderId, string? OccupantName, uint RowVersion);

public record DormitoryRoomDto(
    Guid Id, Guid DormitoryId, string Name, int Capacity, IReadOnlyList<BedDto> Beds, uint RowVersion);

public record DormitoryDetailDto(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, string? Notes, int Capacity, int OccupiedBeds, uint RowVersion,
    IReadOnlyList<DormitoryRoomDto> Rooms);

/// <summary>Résultat d'une chambre créée ou modifiée, avec ses lits.</summary>
public record DormitoryRoomResult(Guid Id, Guid DormitoryId, string Name, IReadOnlyList<BedDto> Beds, uint RowVersion);

public record DeletedBoardingItemDto(Guid Id, string Name, Guid? ParentId, DateTimeOffset? DeletedAt);
