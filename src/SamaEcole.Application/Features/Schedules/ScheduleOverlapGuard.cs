using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.HourVolumes;

namespace SamaEcole.Application.Features.Schedules;

/// <summary>
/// Chevauchement à l'écriture d'un créneau : même enseignant, même classe ou même salle, le même jour, sur une plage
/// qui se recouvre (deux créneaux qui se touchent ne se chevauchent pas). La salle se compare sans casse, accents ni
/// ponctuation (« Salle 12 » = « salle-12 »), comme au contrôle de conformité (<see cref="TimetableCompliance"/>).
/// Renvoie le message à afficher, ou null.
/// </summary>
public static class ScheduleOverlapGuard
{
    public static async Task<string?> FindOverlapAsync(
        IApplicationDbContext context, Guid? excludedSlotId, DayOfWeek day, TimeOnly start, TimeOnly end,
        Guid teacherId, Guid classroomId, string? roomNumber, CancellationToken cancellationToken)
    {
        var overlapping = await context.ScheduleSlots.AsNoTracking()
            .Where(s => s.Id != excludedSlotId && s.DayOfWeek == day && s.StartTime < end && s.EndTime > start)
            .Select(s => new { s.TeacherId, s.ClassroomId, s.RoomNumber })
            .ToListAsync(cancellationToken);

        if (overlapping.Any(s => s.TeacherId == teacherId))
            return "L'enseignant a déjà cours sur cette plage horaire.";

        if (overlapping.Any(s => s.ClassroomId == classroomId))
            return "La classe a déjà cours sur cette plage horaire.";

        var room = TimetableCompliance.RoomKey(roomNumber);
        if (room is not null && overlapping.Any(s => TimetableCompliance.RoomKey(s.RoomNumber) == room))
            return $"La salle « {roomNumber!.Trim()} » est déjà occupée sur cette plage horaire.";

        return null;
    }
}
