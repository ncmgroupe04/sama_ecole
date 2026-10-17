using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.RollBook;

public sealed record RollBookStudent(Guid Id, string Matricule, string FullName, string? FullNameAr);

public sealed record PlacedCell(DateOnly Date, HalfDay Half, RollBookCell Cell);

public sealed record StudentRollBook(RollBookStudent Student, IReadOnlyList<PlacedCell> Cells, AttendanceCounts Counts);

/// <summary>Le cahier d'UNE classe sur une période, prêt à être présenté (grille) ou agrégé (statistiques).</summary>
public sealed record ClassRollBook(
    Guid ClassroomId,
    string Name,
    string Level,
    IReadOnlyList<ClassDay> Days,
    IReadOnlyList<HalfDay> ActiveHalves,
    IReadOnlyList<StudentRollBook> Students,
    int UnplacedSessions)
{
    public int ClassDayCount => Days.Count(d => d.IsClassDay);

    /// <summary>Demi-journées attendues PAR ÉLÈVE : jours de classe × demi-journées où la classe a cours.</summary>
    public int ExpectedHalfDaysPerStudent => ClassDayCount * ActiveHalves.Count;

    public int ExpectedHalfDays => ExpectedHalfDaysPerStudent * Students.Count;

    public AttendanceCounts Totals => AttendanceCounts.Sum(Students.Select(s => s.Counts));
}

/// <summary>
/// Construit le cahier de roulement d'une classe sur une période : fusionne les appels par séance (statut
/// dérivé) et les saisies du cahier (qui priment), sur les seuls jours de classe (spec §2). Partagé par la
/// grille mensuelle, les statistiques et — en 5c — l'export PDF : une seule source de vérité pour les cases
/// ET pour les compteurs. Le Global Query Filter + la RLS bornent tout à l'école courante (règle #2).
/// </summary>
public sealed class RollBookEngine(IApplicationDbContext dbContext, WorkingDayGuard workingDayGuard)
{
    /// <summary>
    /// Année scolaire qui porte la période demandée : celle qui la recouvre, en préférant l'année ACTIVE.
    /// Aucune année ne la recouvre = 422 (un cahier sans année n'a pas de jours de classe).
    /// </summary>
    public async Task<SchoolYear> ResolveYearAsync(DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        var overlapping = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.StartDate <= periodEnd && y.EndDate >= periodStart)
            .ToListAsync(cancellationToken);

        return overlapping.OrderByDescending(y => y.IsActive).ThenBy(y => y.StartDate).FirstOrDefault()
            ?? throw new ValidationException([
                new ValidationFailure("Period",
                    $"Aucune année scolaire ne couvre la période du {periodStart:dd/MM/yyyy} au {periodEnd:dd/MM/yyyy}.")
            ]);
    }

    public async Task<ClassRollBook> BuildAsync(
        Guid classroomId, SchoolYear year, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken)
    {
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => new { c.Id, c.Name, c.Level, c.Cycle })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure("ClassroomId", "La classe indiquée n'existe pas dans votre établissement.")
            ]);

        var workingDays = await workingDayGuard.GetWorkingDaysAsync(cancellationToken);
        var closures = await dbContext.SchoolClosures.AsNoTracking()
            .Where(c => c.SchoolYearId == year.Id)
            .ToListAsync(cancellationToken);

        var days = RollBookCalendar.Build(periodStart, periodEnd, workingDays, year.StartDate, year.EndDate, closures);

        // Roster : mêmes règles que la feuille d'appel — un élève en abandon ou transféré sur l'année sort de la liste.
        var excludedStudentIds = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.SchoolYearId == year.Id
                        && e.ClassroomId == classroomId
                        && (e.Status == EnrollmentStatus.DroppedOut || e.Status == EnrollmentStatus.Transferred))
            .Select(e => e.StudentId)
            .ToListAsync(cancellationToken);

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == classroomId && !excludedStudentIds.Contains(s.Id))
            .OrderBy(s => s.FullName)
            .Select(s => new RollBookStudent(s.Id, s.Matricule, s.FullName, s.FullNameAr))
            .ToListAsync(cancellationToken);

        // Appels par séance : on ne sait placer une séance dans une demi-journée que si elle a un créneau
        // (heure de début). Les autres sont comptées, jamais devinées : la grille signale « n séances non placées ».
        var lines = await (
            from sa in dbContext.StudentAttendances.AsNoTracking()
            join sheet in dbContext.AttendanceSheets.AsNoTracking() on sa.AttendanceSheetId equals sheet.Id
            join slot in dbContext.ScheduleSlots.AsNoTracking() on sheet.ScheduleSlotId equals slot.Id into slots
            from slot in slots.DefaultIfEmpty()
            where sheet.ClassroomId == classroomId && sheet.Date >= periodStart && sheet.Date <= periodEnd
            select new { sa.StudentId, sheet.Date, sa.Status, sa.LateMinutes, Start = (TimeOnly?)slot.StartTime })
            .ToListAsync(cancellationToken);

        var unplaced = lines.Count(l => l.Start is null);

        var derivedByKey = lines
            .Where(l => l.Start is not null)
            .GroupBy(l => (l.StudentId, l.Date, Half: ToHalfDay(l.Start!.Value)))
            .ToDictionary(g => g.Key, g => HalfDayDeriver.Derive(g.Select(l => new SessionLine(l.Status, l.LateMinutes)).ToList()));

        var entries = await dbContext.RollBookEntries.AsNoTracking()
            .Where(e => e.ClassroomId == classroomId && e.Date >= periodStart && e.Date <= periodEnd)
            .ToListAsync(cancellationToken);
        var entryByKey = entries.ToDictionary(e => (e.StudentId, e.Date, e.HalfDay));

        var activeHalves = await ActiveHalvesAsync(classroomId, derivedByKey.Keys.Select(k => k.Half), entries, cancellationToken);

        var rows = new List<StudentRollBook>(students.Count);
        foreach (var student in students)
        {
            var cells = new List<PlacedCell>();
            var classDayCells = new List<RollBookCell>();

            foreach (var day in days)
            {
                foreach (var half in new[] { HalfDay.Morning, HalfDay.Afternoon })
                {
                    var key = (student.Id, day.Date, half);
                    var cell = RollBookCellResolver.Resolve(
                        entryByKey.GetValueOrDefault(key),
                        derivedByKey.GetValueOrDefault(key, RollBookCell.Empty));

                    if (!cell.IsRecorded)
                    {
                        continue;
                    }

                    cells.Add(new PlacedCell(day.Date, half, cell));

                    // Une saisie un jour non travaillé reste affichée (signalée) mais ne pèse pas dans les taux.
                    if (day.IsClassDay)
                    {
                        classDayCells.Add(cell);
                    }
                }
            }

            rows.Add(new StudentRollBook(student, cells, AttendanceCounts.From(classDayCells)));
        }

        var level = ClassroomGradeLevels.FromClassroomName(classroom.Name, classroom.Cycle) ?? classroom.Level.Trim();

        return new ClassRollBook(classroom.Id, classroom.Name, level, days, activeHalves, rows, unplaced);
    }

    /// <summary>
    /// Demi-journées où la classe a cours : celles de son emploi du temps, plus celles où quelque chose a été
    /// renseigné. Sans aucune information, les deux — on ne présume pas qu'une école ne travaille que le matin.
    /// Sert de dénominateur à la couverture : une classe qui n'a cours que le matin n'est pas « à moitié vide ».
    /// </summary>
    private async Task<IReadOnlyList<HalfDay>> ActiveHalvesAsync(
        Guid classroomId, IEnumerable<HalfDay> derivedHalves, IEnumerable<RollBookEntry> entries, CancellationToken cancellationToken)
    {
        var slotStarts = await dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.ClassroomId == classroomId)
            .Select(s => s.StartTime)
            .ToListAsync(cancellationToken);

        var halves = slotStarts.Select(ToHalfDay)
            .Concat(derivedHalves)
            .Concat(entries.Select(e => e.HalfDay))
            .Distinct()
            .OrderBy(h => h)
            .ToList();

        return halves.Count == 0 ? [HalfDay.Morning, HalfDay.Afternoon] : halves;
    }

    public static HalfDay ToHalfDay(TimeOnly start) =>
        SectionScheduleAnalyzer.HalfOf(start) == DayHalf.Morning ? HalfDay.Morning : HalfDay.Afternoon;
}
