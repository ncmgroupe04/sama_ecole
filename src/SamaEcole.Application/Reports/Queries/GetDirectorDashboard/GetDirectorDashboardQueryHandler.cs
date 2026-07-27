using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Reports.Queries.GetDirectorDashboard;

public class GetDirectorDashboardQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<GetDirectorDashboardQuery, DirectorDashboardDto>
{
    public async Task<DirectorDashboardDto> Handle(GetDirectorDashboardQuery request, CancellationToken cancellationToken)
    {
        // ---- Effectifs de l'année ACTIVE, ventilés par genre ----
        // Le SchoolId n'est jamais filtré à la main : le Global Query Filter + la policy RLS bornent
        // déjà chaque requête à l'école courante (AGENTS.md règle #2). Un Super Admin sans tenant voit
        // donc zéro partout, ce qui est le comportement attendu.
        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Élèves ayant une inscription NON ANNULÉE sur l'année active. Distinct sur l'élève : une
        // réinscription corrigée ne doit pas le compter deux fois. Sans année active, la comparaison
        // sur SchoolYearId ne matche rien → effectifs à zéro.
        var enrolledStudents =
            from e in dbContext.Enrollments.AsNoTracking()
            where e.Status != EnrollmentStatus.Cancelled && e.SchoolYearId == activeYearId
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            select s;

        var boys = await enrolledStudents.Where(s => s.Gender == "M").Select(s => s.Id).Distinct().CountAsync(cancellationToken);
        var girls = await enrolledStudents.Where(s => s.Gender == "F").Select(s => s.Id).Distinct().CountAsync(cancellationToken);

        // ---- Ressources humaines : enseignants actifs ----
        var activeTeachers = await dbContext.Teachers.AsNoTracking()
            .CountAsync(t => t.Status == EntityStatus.Active, cancellationToken);

        // ---- Assiduité : taux de présence du mois EN COURS ----
        // "Aujourd'hui" vient de TimeProvider (jamais DateTime.UtcNow en dur), comme le dashboard
        // financier : les tests contrôlent ainsi l'horloge.
        var now = timeProvider.GetUtcNow();
        var monthStart = new DateOnly(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1); // borne haute exclusive

        var monthLines =
            from sa in dbContext.StudentAttendances.AsNoTracking()
            join sheet in dbContext.AttendanceSheets.AsNoTracking() on sa.AttendanceSheetId equals sheet.Id
            where sheet.Date >= monthStart && sheet.Date < monthEnd
            select sa.Status;

        var totalLines = await monthLines.CountAsync(cancellationToken);

        // Un retard est une présence tardive (voir enum AttendanceStatus), il compte donc comme présent.
        var presentLines = await monthLines
            .CountAsync(s => s == AttendanceStatus.Present || s == AttendanceStatus.Late, cancellationToken);

        decimal? attendanceRate = totalLines > 0
            ? Math.Round((decimal)presentLines / totalLines, 4)
            : null;

        // ---- Abonnement ----
        // Subscription implémente ITenantEntity : Global Query Filter EF Core + policy RLS bornent la
        // lecture à l'école de la session. On lit par le SchoolId du JWT (jamais d'un paramètre client,
        // règle #10) ; un Super Admin sans école n'a pas d'abonnement.
        SubscriptionSummaryDto? subscriptionSummary = null;
        if (tenantProvider.CurrentSchoolId is { } schoolId)
        {
            var subscription = await dbContext.Subscriptions.AsNoTracking()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => new { s.Plan, s.Status, s.ExpiresAt })
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription is not null)
            {
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                int? daysRemaining = subscription.ExpiresAt is { } expiresAt
                    ? expiresAt.DayNumber - today.DayNumber
                    : null;

                subscriptionSummary = new SubscriptionSummaryDto(
                    subscription.Plan.ToString(),
                    subscription.Status.ToString(),
                    subscription.ExpiresAt,
                    daysRemaining);
            }
        }

        // ---- Emploi du Temps du Jour ----
        var todayDayOfWeek = now.DayOfWeek;
        var todaySlots = await dbContext.ScheduleSlots.AsNoTracking()
            .Where(s => s.DayOfWeek == todayDayOfWeek)
            .Include(s => s.Teacher)
            .Include(s => s.Subject)
            .Include(s => s.Classroom)
            .ToListAsync(cancellationToken);

        var activeClassroomsCount = await dbContext.Classrooms.AsNoTracking().CountAsync(cancellationToken);
        var expectedSlotsPerClassroom = 8;
        var totalExpectedSlots = activeClassroomsCount * expectedSlotsPerClassroom;

        decimal todayOccupancyRate = totalExpectedSlots > 0 ? Math.Min(1.0m, (decimal)todaySlots.Count / totalExpectedSlots) : 0m;

        var nowTime = TimeOnly.FromTimeSpan(now.TimeOfDay);
        var nextClasses = todaySlots
            .Where(s => s.EndTime > nowTime)
            .OrderBy(s => s.StartTime)
            .Take(4)
            .Select(s => new NextClassDto(
                s.StartTime.ToString("HH:mm"),
                s.EndTime.ToString("HH:mm"),
                s.Subject.Name,
                s.Teacher.FullName,
                s.RoomNumber ?? "-",
                s.Classroom.Name))
            .ToList();

        return new DirectorDashboardDto(
            new EnrollmentStatsDto(boys + girls, boys, girls),
            activeTeachers,
            attendanceRate,
            subscriptionSummary,
            todayOccupancyRate,
            nextClasses);
    }
}
