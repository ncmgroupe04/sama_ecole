using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Boarders.FindBoardableStudents;

/// <summary>
/// GET /api/v1/boarding/boardable-students?term= — recherche des élèves à affecter (modale d'affectation de l'écran Internat, lot G).
/// Restreinte aux inscriptions NON annulées de l'année scolaire ACTIVE : un élève sans inscription de l'année n'a rien à affecter. 2 caractères au
/// moins (en dessous : liste vide), 20 résultats au plus, triés par nom.
///
/// Reprend le périmètre de <c>GET /internat/students/search</c> (route héritée, supprimée au lot F) mais décrit le SÉJOUR, pas l'inscription : la
/// modale appelle <c>POST /boarding/assign-bed</c>, qui attend l'identifiant de l'inscription et — pour un transfert — le jeton <c>xmin</c> du
/// séjour (<see cref="BoardableStudentCandidateDto.RowVersion"/>), jamais celui de l'inscription.
/// </summary>
public record FindBoardableStudentsQuery(string? Term) : IRequest<IReadOnlyList<BoardableStudentCandidateDto>>;

/// <summary>
/// Un élève affectable. <see cref="BoarderId"/>, <see cref="Regime"/>, le lit et <see cref="RowVersion"/> sont nuls pour un élève sans séjour actif
/// (« externe ») ; le lit, la chambre et le pavillon sont nuls pour un demi-pensionnaire ou un interne en attente de lit.
/// </summary>
public record BoardableStudentCandidateDto(
    Guid StudentId,
    Guid EnrollmentId,
    string Matricule,
    string FullName,
    string ClassroomName,
    Guid? BoarderId,
    BoardingRegime? Regime,
    Guid? BedId,
    int? BedNumber,
    string? RoomName,
    string? DormitoryName,
    uint? RowVersion);

public class FindBoardableStudentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<FindBoardableStudentsQuery, IReadOnlyList<BoardableStudentCandidateDto>>
{
    private const int MinTermLength = 2;
    private const int MaxResults = 20;

    public async Task<IReadOnlyList<BoardableStudentCandidateDto>> Handle(
        FindBoardableStudentsQuery request, CancellationToken cancellationToken)
    {
        var term = (request.Term ?? string.Empty).Trim().ToLower();
        if (term.Length < MinTermLength)
        {
            return [];
        }

        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Aucune année active : aucune inscription n'est affectable (comme POST /enrollments, qui exige une année active).
        if (activeYearId is null)
        {
            return [];
        }

        // ToLower() plutôt qu'EF.Functions.ILike : Application reste ignorante du provider (même choix que ListBoardersQuery).
        var students = await (
                from e in dbContext.Enrollments.AsNoTracking()
                join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
                join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
                where e.SchoolYearId == activeYearId
                      && e.Status != EnrollmentStatus.Cancelled
                      && (s.FullName.ToLower().Contains(term) || s.Matricule.ToLower().Contains(term))
                orderby s.FullName, e.Id
                select new { StudentId = s.Id, EnrollmentId = e.Id, s.Matricule, s.FullName, ClassroomName = c.Name })
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        if (students.Count == 0)
        {
            return [];
        }

        var enrollmentIds = students.Select(s => s.EnrollmentId).ToList();

        // Séjour ACTIF de chaque inscription (au plus un : index unique partiel), avec lit → chambre → pavillon quand il y en a un.
        var stays = (await (
                from be in dbContext.BoardingEnrollments.AsNoTracking()
                where be.IsActive && enrollmentIds.Contains(be.EnrollmentId)
                join bg in dbContext.Beds on be.BedId equals bg.Id into beds
                from bed in beds.DefaultIfEmpty()
                join rg in dbContext.DormitoryRooms on bed.DormitoryRoomId equals rg.Id into rooms
                from room in rooms.DefaultIfEmpty()
                join dg in dbContext.Dormitories on room.DormitoryId equals dg.Id into dormitories
                from dormitory in dormitories.DefaultIfEmpty()
                select new
                {
                    be.Id,
                    be.EnrollmentId,
                    be.Regime,
                    be.BedId,
                    BedNumber = bed == null ? (int?)null : bed.BedNumber,
                    RoomName = room == null ? null : room.Name,
                    DormitoryName = dormitory == null ? null : dormitory.Name,
                    RowVersion = EF.Property<uint>(be, "xmin")
                })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => x.EnrollmentId);

        return students
            .Select(s => stays.TryGetValue(s.EnrollmentId, out var stay)
                ? new BoardableStudentCandidateDto(
                    s.StudentId, s.EnrollmentId, s.Matricule, s.FullName, s.ClassroomName,
                    stay.Id, stay.Regime, stay.BedId, stay.BedNumber, stay.RoomName, stay.DormitoryName, stay.RowVersion)
                : new BoardableStudentCandidateDto(
                    s.StudentId, s.EnrollmentId, s.Matricule, s.FullName, s.ClassroomName,
                    null, null, null, null, null, null, null))
            .ToList();
    }
}
