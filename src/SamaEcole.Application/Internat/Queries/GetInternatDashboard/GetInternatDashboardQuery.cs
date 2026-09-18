using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetInternatDashboard;

/// <summary>
/// GET /api/v1/internat/dashboard — tableau de bord de l'Internat (module Internat, spec §5.3) :
/// KPIs globaux et occupation par chambre pour l'année scolaire ACTIVE. LECTURE seule, bornée au
/// tenant courant par le Global Query Filter + la RLS (aucun SchoolId accepté du client).
/// </summary>
public record GetInternatDashboardQuery : IRequest<InternatDashboardDto>;

public record InternatDashboardDto(
    int TotalCapacity,
    int TotalOccupied,
    int InterneCount,
    int DemiPensionnaireCount,
    int FullRoomsCount,
    int RoomsWithFreeSpaceCount,
    IReadOnlyList<DormitoryRoomDto> Rooms);

public record DormitoryRoomDto(
    Guid RoomId,
    string RoomName,
    string BuildingName,
    int Capacity,
    int OccupantsCount,
    IReadOnlyList<BoardingOccupantDto> Occupants);

public record BoardingOccupantDto(
    Guid StudentId,
    Guid EnrollmentId,
    string FullName,
    string ClassroomName,
    string? GuardianPhone,
    string BoardingStatus);

public class GetInternatDashboardQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetInternatDashboardQuery, InternatDashboardDto>
{
    public async Task<InternatDashboardDto> Handle(GetInternatDashboardQuery request, CancellationToken cancellationToken)
    {
        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var dormitories = await dbContext.Rooms.AsNoTracking()
            .Where(r => r.Type == RoomType.Dortoir)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Capacity,
                BuildingName = dbContext.Buildings.AsNoTracking()
                    .Where(b => b.Id == r.BuildingId)
                    .Select(b => b.Name)
                    .FirstOrDefault() ?? "Bâtiment supprimé"
            })
            .ToListAsync(cancellationToken);

        // Aucune année active : aucun élève ne peut être affecté (CreateEnrollmentCommandHandler
        // exige déjà une année active), donc toutes les chambres apparaissent vides plutôt que
        // de lever une erreur — l'écran doit rester consultable pour créer des dortoirs en amont.
        var occupants = activeYearId is null
            ? []
            : await (
                from e in dbContext.Enrollments.AsNoTracking()
                join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
                join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
                where e.SchoolYearId == activeYearId && e.Status != Domain.Enums.EnrollmentStatus.Cancelled
                      && e.RoomId != null
                select new
                {
                    RoomId = e.RoomId!.Value,
                    StudentId = s.Id,
                    EnrollmentId = e.Id,
                    s.FullName,
                    ClassroomName = c.Name,
                    s.GuardianPhone,
                    BoardingStatus = e.BoardingStatus.ToString()
                })
                .ToListAsync(cancellationToken);

        var occupantsByRoom = occupants.ToLookup(o => o.RoomId);

        var rooms = dormitories
            .Select(r =>
            {
                var roomOccupants = occupantsByRoom[r.Id]
                    .Select(o => new BoardingOccupantDto(
                        o.StudentId, o.EnrollmentId, o.FullName, o.ClassroomName, o.GuardianPhone, o.BoardingStatus))
                    .OrderBy(o => o.FullName)
                    .ToList();

                return new DormitoryRoomDto(r.Id, r.Name, r.BuildingName, r.Capacity, roomOccupants.Count, roomOccupants);
            })
            .OrderBy(r => r.BuildingName).ThenBy(r => r.RoomName)
            .ToList();

        return new InternatDashboardDto(
            TotalCapacity: rooms.Sum(r => r.Capacity),
            TotalOccupied: rooms.Sum(r => r.OccupantsCount),
            InterneCount: occupants.Count(o => o.BoardingStatus == nameof(BoardingStatus.Interne)),
            DemiPensionnaireCount: occupants.Count(o => o.BoardingStatus == nameof(BoardingStatus.DemiPensionnaire)),
            FullRoomsCount: rooms.Count(r => r.OccupantsCount >= r.Capacity),
            RoomsWithFreeSpaceCount: rooms.Count(r => r.OccupantsCount < r.Capacity),
            Rooms: rooms);
    }
}
