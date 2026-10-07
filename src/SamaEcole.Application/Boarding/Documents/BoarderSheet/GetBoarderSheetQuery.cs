using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Application.Boarding.Boarders;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Documents.BoarderSheet;

/// <summary>
/// Données d'impression de la fiche individuelle d'un pensionnaire. <see cref="MedicalNotes"/> est une donnée de santé d'un
/// mineur : renseignée pour le Directeur et le Surveillant seulement, <c>null</c> pour tout autre rôle (spec §5.2). Le DTO
/// est masqué AVANT d'arriver au moteur PDF : le document n'a donc rien à cacher, il imprime ce qu'on lui donne.
/// </summary>
public record BoarderSheetDto(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolLogoUrl,
    string? SchoolYearLabel,
    BoarderListItemDto Boarder,
    string? FullNameAr,
    DateOnly BirthDate,
    string BirthPlace,
    string? MedicalNotes,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    IReadOnlyList<AllowedExitPersonDto> AllowedExitPersons,
    IReadOnlyList<BoardingLeaveDto> Leaves,
    DateTimeOffset GeneratedAt);

/// <summary>Fiche imprimable d'un séjour, actif ou clos (archive). 404 si le séjour n'est pas de l'école courante.</summary>
public record GetBoarderSheetQuery(Guid BoarderId) : IRequest<BoarderSheetDto>;

public class GetBoarderSheetQueryHandler(
    IApplicationDbContext dbContext, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<GetBoarderSheetQuery, BoarderSheetDto>
{
    public async Task<BoarderSheetDto> Handle(GetBoarderSheetQuery request, CancellationToken cancellationToken)
    {
        // La règle de masquage est celle de GET /boarders/{id} : une seule définition, jamais une relecture de MedicalNotes.
        var detail = await BoarderDetailReader.GetAsync(dbContext, request.BoarderId, currentUser.Role, timeProvider, cancellationToken);

        var student = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == detail.Boarder.StudentId)
            .Select(s => new { s.FullNameAr, s.BirthDate, s.BirthPlace, s.SchoolId })
            .FirstAsync(cancellationToken);

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == student.SchoolId)
            .Select(s => new { s.Name, s.Address, s.LogoUrl })
            .FirstAsync(cancellationToken);

        var year = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => new { y.Label, y.StartDate, y.EndDate })
            .FirstOrDefaultAsync(cancellationToken);

        // Toutes les sorties de l'année active (et non les 10 de la fiche JSON), la plus récente d'abord.
        var leaves = year is null
            ? []
            : (await LeaveReader.Project(dbContext, dbContext.BoardingLeaves.AsNoTracking()
                        .Where(l => l.BoardingEnrollmentId == request.BoarderId
                                    && l.LeaveDate <= year.EndDate && l.ExpectedReturnDate >= year.StartDate))
                    .ToListAsync(cancellationToken))
                .Select(l => LeaveReader.ToDto(l, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)))
                .ToList();

        return new BoarderSheetDto(
            school.Name, school.Address, school.LogoUrl, year?.Label,
            detail.Boarder, student.FullNameAr, student.BirthDate, student.BirthPlace,
            detail.MedicalNotes, detail.EmergencyContactName, detail.EmergencyContactPhone,
            detail.AllowedExitPersons, leaves, timeProvider.GetUtcNow());
    }
}
