using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.GetInternatDashboard;

/// <summary>
/// GET /api/v1/internat/dashboard — tableau de bord de l'Internat de l'ANCIEN écran : KPIs globaux et occupation par
/// chambre pour l'année scolaire ACTIVE. LECTURE seule, bornée au tenant courant par le Global Query Filter + la RLS.
///
/// Depuis le lot C (modèle Pavillon/Lit), les données viennent des SÉJOURS (<c>boarding_enrollments</c>) et non plus de
/// <c>Enrollment.RoomId</c>. Le JSON est INCHANGÉ pour que <c>internat.js</c> et <c>enrollments.js</c> n'aient pas à
/// bouger : <c>roomId</c> est l'identifiant d'une <c>DormitoryRoom</c> (identique à l'ancien <c>Room.Id</c> pour les chambres
/// reprises), <c>buildingName</c> le nom du PAVILLON, <c>capacity</c> le nombre de lits hors maintenance. Un
/// demi-pensionnaire n'occupe aucune chambre : il n'apparaît que dans <c>demiPensionnaireCount</c>.
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

        var rooms = await (
            from r in dbContext.DormitoryRooms.AsNoTracking()
            join d in dbContext.Dormitories.AsNoTracking() on r.DormitoryId equals d.Id
            select new { r.Id, r.Name, DormitoryName = d.Name })
            .ToListAsync(cancellationToken);

        // Capacité d'une chambre = ses lits UTILISABLES (hors maintenance), comme l'occupation est comptée sur des lits.
        var capacityByRoom = (await dbContext.Beds.AsNoTracking()
                .Where(b => b.Status != BedStatus.Maintenance)
                .GroupBy(b => b.DormitoryRoomId)
                .Select(g => new { RoomId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.RoomId, x => x.Count);

        // Aucune année active : aucun élève ne peut être affecté (l'inscription exige déjà une année active), donc toutes les
        // chambres apparaissent vides plutôt que de lever une erreur — l'écran reste consultable pour préparer l'année.
        var stays = activeYearId is null
            ? []
            : await (
                from be in dbContext.BoardingEnrollments.AsNoTracking()
                where be.IsActive
                join e in dbContext.Enrollments.AsNoTracking() on be.EnrollmentId equals e.Id
                where e.SchoolYearId == activeYearId && e.Status != EnrollmentStatus.Cancelled
                join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
                join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
                select new
                {
                    RoomId = dbContext.Beds.Where(b => b.Id == be.BedId)
                        .Select(b => (Guid?)b.DormitoryRoomId).FirstOrDefault(),
                    StudentId = s.Id,
                    EnrollmentId = e.Id,
                    s.FullName,
                    ClassroomName = c.Name,
                    s.GuardianPhone,
                    be.Regime
                })
                .ToListAsync(cancellationToken);

        var seated = stays.Where(x => x.Regime == BoardingRegime.Interne && x.RoomId != null).ToList();
        var occupantsByRoom = seated.ToLookup(x => x.RoomId!.Value);

        var roomDtos = rooms
            .Select(r =>
            {
                var occupants = occupantsByRoom[r.Id]
                    .Select(o => new BoardingOccupantDto(
                        o.StudentId, o.EnrollmentId, o.FullName, o.ClassroomName, o.GuardianPhone, nameof(BoardingStatus.Interne)))
                    .OrderBy(o => o.FullName)
                    .ToList();

                return new DormitoryRoomDto(
                    r.Id, r.Name, r.DormitoryName, capacityByRoom.GetValueOrDefault(r.Id), occupants.Count, occupants);
            })
            .OrderBy(r => r.BuildingName).ThenBy(r => r.RoomName)
            .ToList();

        return new InternatDashboardDto(
            TotalCapacity: roomDtos.Sum(r => r.Capacity),
            TotalOccupied: roomDtos.Sum(r => r.OccupantsCount),
            InterneCount: stays.Count(x => x.Regime == BoardingRegime.Interne),
            DemiPensionnaireCount: stays.Count(x => x.Regime == BoardingRegime.DemiPensionnaire),
            FullRoomsCount: roomDtos.Count(r => r.OccupantsCount >= r.Capacity),
            RoomsWithFreeSpaceCount: roomDtos.Count(r => r.OccupantsCount < r.Capacity),
            Rooms: roomDtos);
    }
}
