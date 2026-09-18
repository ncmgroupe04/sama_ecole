using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.SearchBoardableStudents;

/// <summary>
/// GET /api/v1/internat/students/search?term= — autocomplétion élève pour la modale d'affectation
/// (spec §5.4). Restreint aux inscriptions ACTIVES de l'année en cours : un élève sans inscription
/// active n'a rien à affecter. Retourne le régime et la chambre ACTUELS pour le badge de statut et
/// l'avertissement de transfert côté UI.
/// </summary>
public record SearchBoardableStudentsQuery(string SearchTerm) : IRequest<IReadOnlyList<BoardableStudentDto>>;

/// <summary>
/// <see cref="RowVersion"/> est le jeton xmin RÉEL de l'inscription (AGENTS.md règle #5), lu comme
/// <c>AcademicHistoryEntryDto.RowVersion</c> (GetStudentDetailQuery) — nécessaire à la modale
/// d'affectation (Task 16) pour poser SetOriginalConcurrencyToken sur ChangeBoardingAssignmentCommand
/// sans provoquer un 409 systématique dès qu'une inscription a déjà été modifiée une fois.
/// </summary>
public record BoardableStudentDto(
    Guid StudentId,
    Guid EnrollmentId,
    string Matricule,
    string FullName,
    string ClassroomName,
    string BoardingStatus,
    Guid? CurrentRoomId,
    string? CurrentRoomName,
    uint RowVersion);

public class SearchBoardableStudentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<SearchBoardableStudentsQuery, IReadOnlyList<BoardableStudentDto>>
{
    public async Task<IReadOnlyList<BoardableStudentDto>> Handle(
        SearchBoardableStudentsQuery request, CancellationToken cancellationToken)
    {
        var search = request.SearchTerm.Trim().ToLower();
        if (search.Length < 2) return [];

        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Aucune année active : aucune inscription n'est "active", donc rien à affecter — cohérent
        // avec CreateEnrollmentCommandHandler qui exige déjà une année active pour ce faire.
        if (activeYearId is null) return [];

        // Même remarque que SearchStudentsForCashierQueryHandler / GetStudentsQueryHandler : ToLower()
        // plutôt qu'EF.Functions.ILike (Npgsql), pour qu'Application reste ignorante du provider
        // (Clean Architecture).
        var results = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            where e.SchoolYearId == activeYearId
                  && e.Status != EnrollmentStatus.Cancelled
                  && (s.FullName.ToLower().Contains(search) || s.Matricule.ToLower().Contains(search))
            select new
            {
                s.Id,
                EnrollmentId = e.Id,
                s.Matricule,
                s.FullName,
                ClassroomName = c.Name,
                e.BoardingStatus,
                e.RoomId,
                RowVersion = EF.Property<uint>(e, "xmin")
            })
            .OrderBy(r => r.FullName)
            .Take(20)
            .ToListAsync(cancellationToken);

        var roomIds = results.Where(r => r.RoomId is not null).Select(r => r.RoomId!.Value).Distinct().ToList();
        var roomNames = roomIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Rooms.AsNoTracking()
                .Where(r => roomIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return results
            .Select(r => new BoardableStudentDto(
                r.Id, r.EnrollmentId, r.Matricule, r.FullName, r.ClassroomName, r.BoardingStatus.ToString(),
                r.RoomId, r.RoomId is { } id ? roomNames.GetValueOrDefault(id) : null, r.RowVersion))
            .ToList();
    }
}
