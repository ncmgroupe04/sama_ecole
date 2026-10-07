using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Boarders;

/// <summary>
/// Projection UNIQUE d'un séjour en <see cref="BoarderListItemDto"/> : liste, fiche et résultat d'écriture lisent la même
/// forme. Le Global Query Filter + la RLS bornent déjà tout au tenant courant.
/// </summary>
public static class BoarderReader
{
    public static IQueryable<BoarderListItemDto> Project(IApplicationDbContext dbContext, IQueryable<BoardingEnrollment> stays) =>
        from be in stays
        join s in dbContext.Students on be.StudentId equals s.Id
        join e in dbContext.Enrollments on be.EnrollmentId equals e.Id
        join bg in dbContext.Beds on be.BedId equals bg.Id into beds
        from bed in beds.DefaultIfEmpty()
        join rg in dbContext.DormitoryRooms on bed.DormitoryRoomId equals rg.Id into rooms
        from room in rooms.DefaultIfEmpty()
        join dg in dbContext.Dormitories on room.DormitoryId equals dg.Id into dormitories
        from dormitory in dormitories.DefaultIfEmpty()
        select new BoarderListItemDto(
            be.Id,
            s.Id,
            e.Id,
            s.FullName,
            s.Matricule,
            // Une classe supprimée (soft delete) sort du filtre : on l'affiche explicitement plutôt que de faire
            // disparaître le pensionnaire de la liste (même principe que GetStudentDetailQuery).
            dbContext.Classrooms.IgnoreQueryFilters()
                .Where(c => c.Id == e.ClassroomId && c.SchoolId == e.SchoolId)
                .Select(c => c.Name)
                .FirstOrDefault() ?? "Classe supprimée",
            be.Regime,
            be.IsActive,
            be.BedId,
            bed == null ? null : (int?)bed.BedNumber,
            room == null ? null : (Guid?)room.Id,
            room == null ? null : room.Name,
            dormitory == null ? null : (Guid?)dormitory.Id,
            dormitory == null ? null : dormitory.Name,
            be.StartDate,
            be.EndDate,
            EF.Property<uint>(be, "xmin"));

    public static async Task<BoarderListItemDto> GetItemAsync(
        IApplicationDbContext dbContext, Guid stayId, CancellationToken cancellationToken) =>
        await Project(dbContext, dbContext.BoardingEnrollments.AsNoTracking().Where(b => b.Id == stayId))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new KeyNotFoundException($"Séjour {stayId} introuvable.");
}
