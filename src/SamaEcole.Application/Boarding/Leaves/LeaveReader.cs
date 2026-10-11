using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Leaves;

/// <summary>Ligne brute d'une sortie : tout sauf le statut, qui dépend de la date du jour et se calcule en mémoire.</summary>
internal record LeaveRow(
    Guid Id,
    Guid BoarderId,
    Guid StudentId,
    string StudentName,
    string Matricule,
    string? DormitoryName,
    string? RoomName,
    BoardingLeaveReason Reason,
    string? ReasonDetail,
    DateOnly LeaveDate,
    DateOnly ExpectedReturnDate,
    DateOnly? ActualReturnDate,
    string AccompaniedBy,
    bool IsCompanionUnlisted,
    uint RowVersion);

/// <summary>
/// Projection UNIQUE d'une sortie en <see cref="BoardingLeaveDto"/> : déclaration, retour, liste et fiche pensionnaire lisent
/// la même forme. Le Global Query Filter + la RLS bornent déjà tout au tenant courant.
/// </summary>
internal static class LeaveReader
{
    public static IQueryable<LeaveRow> Project(IApplicationDbContext dbContext, IQueryable<BoardingLeave> leaves) =>
        from l in leaves
        join be in dbContext.BoardingEnrollments on l.BoardingEnrollmentId equals be.Id
        join s in dbContext.Students on be.StudentId equals s.Id
        join bg in dbContext.Beds on be.BedId equals bg.Id into beds
        from bed in beds.DefaultIfEmpty()
        join rg in dbContext.DormitoryRooms on bed.DormitoryRoomId equals rg.Id into rooms
        from room in rooms.DefaultIfEmpty()
        join dg in dbContext.Dormitories on room.DormitoryId equals dg.Id into dormitories
        from dormitory in dormitories.DefaultIfEmpty()
        // Tri DANS la projection : EF ne traduit pas un OrderBy appliqué après la construction du record.
        orderby l.LeaveDate descending, l.Id
        select new LeaveRow(
            l.Id,
            be.Id,
            s.Id,
            s.FullName,
            s.Matricule,
            dormitory == null ? null : dormitory.Name,
            room == null ? null : room.Name,
            l.Reason,
            l.ReasonDetail,
            l.LeaveDate,
            l.ExpectedReturnDate,
            l.ActualReturnDate,
            l.AccompaniedBy,
            l.IsCompanionUnlisted,
            EF.Property<uint>(l, "xmin"));

    public static BoardingLeaveDto ToDto(LeaveRow row, DateOnly today) =>
        new(
            row.Id, row.BoarderId, row.StudentId, row.StudentName, row.Matricule, row.DormitoryName, row.RoomName,
            row.Reason, row.ReasonDetail, row.LeaveDate, row.ExpectedReturnDate, row.ActualReturnDate,
            row.AccompaniedBy, row.IsCompanionUnlisted,
            BoardingLeaveStatus.Of(row.LeaveDate, row.ExpectedReturnDate, row.ActualReturnDate, today),
            row.RowVersion);

    public static async Task<BoardingLeaveDto> GetAsync(
        IApplicationDbContext dbContext, Guid leaveId, DateOnly today, CancellationToken cancellationToken)
    {
        var row = await Project(dbContext, dbContext.BoardingLeaves.AsNoTracking().Where(l => l.Id == leaveId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Sortie {leaveId} introuvable.");

        return ToDto(row, today);
    }
}
