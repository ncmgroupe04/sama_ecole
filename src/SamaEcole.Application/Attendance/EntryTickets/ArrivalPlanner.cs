using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Attendance.EntryTickets;

/// <summary>Un cours de la journée de la classe, avec de quoi l'afficher (aperçu et billet imprimé).</summary>
public sealed record PlannedSlot(
    Guid SlotId, Guid SubjectId, string SubjectName, string TeacherName, TimeOnly Start, TimeOnly End)
{
    public string Label => SlotPeriod.Label(Start, End);
    public int Minutes => (int)(End - Start).TotalMinutes;
}

/// <summary>Résultat du calcul + les cours retenus, indexés — pour que l'appelant n'ait pas à les relire.</summary>
public sealed record ArrivalPlan(ArrivalCoverageResult Coverage, IReadOnlyDictionary<Guid, PlannedSlot> Slots);

/// <summary>
/// Situe une heure d'arrivée dans la journée de la CLASSE de l'élève (Complément N°5 bis) : charge les cours du
/// jour, écarte ceux qu'un billet actif de cet élève couvre déjà (ils ne sont ni re-justifiés ni re-visés — c'est
/// aussi ce qui évite la violation de l'index unique d'un billet actif par élève, cours et jour), puis délègue le
/// calcul pur à <see cref="ArrivalCoverage"/>. Partagé par l'ÉMISSION du billet et par l'APERÇU de l'écran : les
/// deux affichent forcément la même chose, et le client ne recalcule rien.
///
/// Un jour de repos est refusé (422, comme l'appel) : le billet reste possible SANS heure d'arrivée, minutes
/// saisies à la main (arbitrage D4 de l'Évolution N°3 / B10).
/// </summary>
public class ArrivalPlanner(IApplicationDbContext dbContext, WorkingDayGuard workingDayGuard)
{
    public async Task<ArrivalPlan> PlanAsync(
        Guid classroomId, Guid studentId, DateOnly date, TimeOnly arrival, CancellationToken cancellationToken)
    {
        await workingDayGuard.EnsureWorkingDayAsync(date, "Date", cancellationToken);

        var dayOfWeek = date.DayOfWeek;
        var rows = await dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.ClassroomId == classroomId && s.DayOfWeek == dayOfWeek)
            .OrderBy(s => s.StartTime)
            .Select(s => new
            {
                s.Id, s.SubjectId, SubjectName = s.Subject.Name, TeacherName = s.Teacher.FullName, s.StartTime, s.EndTime
            })
            .ToListAsync(cancellationToken);

        var slots = rows
            .Select(r => new PlannedSlot(r.Id, r.SubjectId, r.SubjectName, r.TeacherName, r.StartTime, r.EndTime))
            .ToList();

        // Cours déjà couverts par un billet ACTIF de cet élève ce jour-là (visés ou manqués).
        var day = date.ToDateTime(TimeOnly.MinValue);
        var activeTickets = await dbContext.LateArrivals.AsNoTracking()
            .Where(l => l.StudentId == studentId
                        && l.Date == day
                        && (l.Status == EntryTicketStatus.Issued || l.Status == EntryTicketStatus.Accepted))
            .Select(l => new { l.TargetScheduleSlotId, l.MissedScheduleSlotIds })
            .ToListAsync(cancellationToken);

        var covered = new HashSet<Guid>();
        foreach (var ticket in activeTickets)
        {
            if (ticket.TargetScheduleSlotId is { } target) covered.Add(target);
            foreach (var missed in ticket.MissedScheduleSlotIds ?? []) covered.Add(missed);
        }

        var open = slots.Where(s => !covered.Contains(s.SlotId)).ToList();
        if (slots.Count > 0 && open.Count == 0)
        {
            throw Refused("Tous les cours de ce jour sont déjà couverts par un billet d'entrée actif de cet élève.");
        }

        var coverage = ArrivalCoverage.Compute(
            open.Select(s => new ArrivalSlot(s.SlotId, s.Start, s.End)).ToList(), arrival);

        // Même borne que la saisie de l'appel : un « retard » de plus de 4 h est une absence, pas un retard.
        if (coverage.LateMinutes > SubmitAttendanceSheetCommandValidator.MaxLateMinutes)
        {
            throw Refused(
                $"Un retard de plus de {SubmitAttendanceSheetCommandValidator.MaxLateMinutes} minutes sur un même cours " +
                "n'est pas un retard : vérifiez l'heure d'arrivée ou l'emploi du temps de la classe.");
        }

        return new ArrivalPlan(coverage, open.ToDictionary(s => s.SlotId));
    }

    private static ValidationException Refused(string message)
        => new([new ValidationFailure("ArrivalTime", message)]);
}
