using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Deletion;

/// <summary>Ce qu'on supprime dans l'internat : un pavillon (avec ses chambres et lits), une chambre (avec ses lits) ou un lit.</summary>
public enum BoardingDeletionKind
{
    Dormitory,
    Room,
    Bed
}

/// <summary>Un élève qui occupe un lit de l'élément à supprimer. <c>RowVersion</c> = jeton de son SÉJOUR, requis pour le réaffecter.</summary>
public record BoardingDeletionOccupantDto(
    Guid BoarderId, Guid EnrollmentId, string StudentName, string Matricule, string Location, uint RowVersion);

/// <summary>Un lit libre HORS de l'élément à supprimer, proposé pour réaffecter un élève avant la suppression.</summary>
public record BoardingFreeBedDto(Guid BedId, string Label);

/// <summary>Analyse AVANT suppression (lecture seule) : les liens à rompre et de quoi réaffecter les élèves d'abord.</summary>
public record BoardingDeletionImpactDto(
    string Label, int RoomCount, int BedCount,
    IReadOnlyList<BoardingDeletionOccupantDto> Occupants, IReadOnlyList<BoardingFreeBedDto> FreeBeds);

/// <summary>
/// GET /api/v1/boarding/deletion-impact?kind=&amp;id= — ce que supprimer cet élément entraîne : chambres et lits rattachés, élèves qui
/// y dorment, et les lits libres ailleurs où les réaffecter d'abord. Ne modifie rien.
/// </summary>
public record GetBoardingDeletionImpactQuery(BoardingDeletionKind Kind, Guid Id) : IRequest<BoardingDeletionImpactDto>;

/// <summary>
/// POST /api/v1/boarding/deletions — « Rompre les liens et supprimer » : l'élément, ses chambres et ses lits sont archivés
/// (suppression logique, règle #6, restaurables) ; les élèves qui y dormaient gardent leur séjour mais n'ont plus de lit — ils
/// redeviennent « en attente de lit » et peuvent être réaffectés. Rien n'est effacé, aucune pension n'est retranchée.
/// </summary>
public record BreakLinksAndDeleteCommand(BoardingDeletionKind Kind, Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class BoardingDeletionValidators : AbstractValidator<BreakLinksAndDeleteCommand>
{
    public BoardingDeletionValidators()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
    }
}

/// <summary>Périmètre d'une suppression : l'élément visé et tout ce qui lui est rattaché (chambres, lits).</summary>
internal sealed record BoardingDeletionScope(
    string Label, object Target, Dormitory? Dormitory, IReadOnlyList<DormitoryRoom> Rooms, IReadOnlyList<Bed> Beds)
{
    public static async Task<BoardingDeletionScope> ResolveAsync(
        IApplicationDbContext db, BoardingDeletionKind kind, Guid id, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case BoardingDeletionKind.Dormitory:
            {
                var dormitory = await db.Dormitories.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                    ?? throw new KeyNotFoundException($"Pavillon {id} introuvable.");
                var rooms = await db.DormitoryRooms.Where(r => r.DormitoryId == id).ToListAsync(cancellationToken);
                var roomIds = rooms.Select(r => r.Id).ToList();
                var beds = await db.Beds.Where(b => roomIds.Contains(b.DormitoryRoomId)).ToListAsync(cancellationToken);
                return new BoardingDeletionScope(dormitory.Name, dormitory, dormitory, rooms, beds);
            }
            case BoardingDeletionKind.Room:
            {
                var room = await db.DormitoryRooms.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                    ?? throw new KeyNotFoundException($"Chambre {id} introuvable.");
                var beds = await db.Beds.Where(b => b.DormitoryRoomId == id).ToListAsync(cancellationToken);
                return new BoardingDeletionScope(room.Name, room, null, [room], beds);
            }
            default:
            {
                var bed = await db.Beds.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                    ?? throw new KeyNotFoundException($"Lit {id} introuvable.");
                return new BoardingDeletionScope($"Lit {bed.BedNumber}", bed, null, [], [bed]);
            }
        }
    }
}

public class GetBoardingDeletionImpactQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetBoardingDeletionImpactQuery, BoardingDeletionImpactDto>
{
    public async Task<BoardingDeletionImpactDto> Handle(GetBoardingDeletionImpactQuery request, CancellationToken cancellationToken)
    {
        var scope = await BoardingDeletionScope.ResolveAsync(dbContext, request.Kind, request.Id, cancellationToken);
        var bedIds = scope.Beds.Select(b => b.Id).ToList();

        var occupants = await (from be in dbContext.BoardingEnrollments.AsNoTracking()
                               join s in dbContext.Students on be.StudentId equals s.Id
                               join b in dbContext.Beds on be.BedId equals b.Id
                               join r in dbContext.DormitoryRooms on b.DormitoryRoomId equals r.Id
                               where be.IsActive && be.BedId != null && bedIds.Contains(be.BedId.Value)
                               orderby r.Name, b.BedNumber
                               select new
                               {
                                   be.Id, be.EnrollmentId, s.FullName, s.Matricule, RoomName = r.Name, b.BedNumber,
                                   RowVersion = EF.Property<uint>(be, "xmin")
                               }).ToListAsync(cancellationToken);

        var freeBeds = new List<BoardingFreeBedDto>();
        if (occupants.Count > 0)
        {
            var held = BoardingOccupancy.ActiveBedIds(dbContext);
            var rows = await (from b in dbContext.Beds.AsNoTracking()
                              join r in dbContext.DormitoryRooms on b.DormitoryRoomId equals r.Id
                              join d in dbContext.Dormitories on r.DormitoryId equals d.Id
                              where b.Status == BedStatus.Available && !held.Contains(b.Id) && !bedIds.Contains(b.Id)
                              orderby d.Name, r.Name, b.BedNumber
                              select new { b.Id, DormitoryName = d.Name, RoomName = r.Name, b.BedNumber })
                .Take(300).ToListAsync(cancellationToken);
            freeBeds = rows.Select(x => new BoardingFreeBedDto(x.Id, $"{x.DormitoryName} — {x.RoomName} — Lit {x.BedNumber}")).ToList();
        }

        return new BoardingDeletionImpactDto(
            scope.Label, scope.Rooms.Count, scope.Beds.Count,
            occupants.Select(o => new BoardingDeletionOccupantDto(
                o.Id, o.EnrollmentId, o.FullName, o.Matricule, $"{o.RoomName} — Lit {o.BedNumber}", o.RowVersion)).ToList(),
            freeBeds);
    }
}

public class BreakLinksAndDeleteCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<BreakLinksAndDeleteCommand, Unit>
{
    public async Task<Unit> Handle(BreakLinksAndDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = (currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.")).ToString();

        var scope = await BoardingDeletionScope.ResolveAsync(dbContext, request.Kind, request.Id, cancellationToken);

        // Posé AVANT toute modification : un jeton périmé (élément modifié entre-temps) rejette l'écriture entière en 409.
        dbContext.SetOriginalConcurrencyToken(scope.Target, request.RowVersion);

        // Les élèves qui dormaient dans ces lits gardent leur séjour actif mais perdent leur lit : « en attente de lit ».
        var bedIds = scope.Beds.Select(b => b.Id).ToList();
        var stays = await dbContext.BoardingEnrollments
            .Where(b => b.IsActive && b.BedId != null && bedIds.Contains(b.BedId.Value))
            .ToListAsync(cancellationToken);
        foreach (var stay in stays)
        {
            stay.BedId = null;
        }

        foreach (var bed in scope.Beds)
        {
            bed.SoftDelete(actor);
        }

        foreach (var room in scope.Rooms)
        {
            room.SoftDelete(actor);
        }

        scope.Dormitory?.SoftDelete(actor);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
