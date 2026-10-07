using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Boarders.ListBoarders;

/// <summary>
/// GET /api/v1/boarding/boarders — pensionnaires de l'année scolaire ACTIVE (décision D10). <see cref="Status"/> :
/// <c>active</c> (défaut), <c>ended</c> (séjours clos de l'année) ou <c>awaitingBed</c> (internes actifs sans lit).
/// Le filtre « en permission » arrive avec les sorties (lot D).
/// </summary>
public record ListBoardersQuery : IRequest<PaginatedBoarders>
{
    public Guid? DormitoryId { get; init; }
    public Guid? RoomId { get; init; }
    public BoardingRegime? Regime { get; init; }
    public string Status { get; init; } = "active";
    public string? Search { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class ListBoardersQueryValidator : AbstractValidator<ListBoardersQuery>
{
    private static readonly string[] Statuses = ["active", "ended", "awaitingBed"];

    public ListBoardersQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => Statuses.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Statut inconnu : choisissez active, ended ou awaitingBed.");
        RuleFor(x => x.Regime).IsInEnum().When(x => x.Regime is not null);
    }
}

public class ListBoardersQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListBoardersQuery, PaginatedBoarders>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedBoarders> Handle(ListBoardersQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Aucune année active : rien à lister, jamais une exception (l'écran doit rester consultable).
        if (activeYearId is null)
        {
            return new PaginatedBoarders([], 0, page, pageSize);
        }

        var stays = from be in dbContext.BoardingEnrollments.AsNoTracking()
                    join e in dbContext.Enrollments on be.EnrollmentId equals e.Id
                    where e.SchoolYearId == activeYearId
                    select be;

        stays = request.Status.ToLowerInvariant() switch
        {
            "ended" => stays.Where(b => !b.IsActive),
            "awaitingbed" => stays.Where(b => b.IsActive && b.Regime == BoardingRegime.Interne && b.BedId == null),
            _ => stays.Where(b => b.IsActive)
        };

        if (request.Regime is { } regime)
        {
            stays = stays.Where(b => b.Regime == regime);
        }

        if (request.RoomId is { } roomId)
        {
            stays = stays.Where(b => dbContext.Beds.Any(bed => bed.Id == b.BedId && bed.DormitoryRoomId == roomId));
        }

        if (request.DormitoryId is { } dormitoryId)
        {
            stays = stays.Where(b => (from bed in dbContext.Beds
                                      join r in dbContext.DormitoryRooms on bed.DormitoryRoomId equals r.Id
                                      where bed.Id == b.BedId && r.DormitoryId == dormitoryId
                                      select bed.Id).Any());
        }

        // Même remarque que SearchBoardableStudents : ToLower() plutôt qu'EF.Functions.ILike, pour qu'Application reste
        // ignorante du provider. Un seul caractère ne filtre pas (trop large pour être utile).
        var term = request.Search?.Trim().ToLower();
        if (term is { Length: >= 2 })
        {
            stays = stays.Where(b => dbContext.Students.Any(
                s => s.Id == b.StudentId && (s.FullName.ToLower().Contains(term) || s.Matricule.ToLower().Contains(term))));
        }

        var total = await stays.CountAsync(cancellationToken);

        var items = await BoarderReader.Project(dbContext, stays)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PaginatedBoarders(items, total, page, pageSize);
    }
}
