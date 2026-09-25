using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Attendance;

/// <summary>
/// Appel par créneau d'emploi du temps (Évolution N°5, arbitrage B2). Le libellé du créneau est DÉRIVÉ du
/// cours — jamais saisi — pour que l'index unique (classe, matière, date, créneau), les rapports et les
/// notifications, qui lisent tous <c>Period</c>, restent inchangés.
/// </summary>
public static class SlotPeriod
{
    public static string Label(TimeOnly start, TimeOnly end) => $"{start:HH\\:mm}-{end:HH\\:mm}";

    /// <summary>Message français si le créneau ne convient pas à (classe, matière, date), sinon null.</summary>
    public static string? Mismatch(ScheduleSlot slot, Guid classroomId, Guid subjectId, DateOnly date)
    {
        if (slot.ClassroomId != classroomId) return "Ce créneau n'appartient pas à la classe indiquée.";
        if (slot.SubjectId != subjectId) return "Ce créneau n'est pas celui de la matière indiquée.";
        if (slot.DayOfWeek != date.DayOfWeek)
        {
            return $"Ce créneau a lieu le {SchoolWeek.FrenchName(slot.DayOfWeek)}, pas le {SchoolWeek.FrenchName(date.DayOfWeek)} {date:dd/MM/yyyy}.";
        }

        return null;
    }
}
