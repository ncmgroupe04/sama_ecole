using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Reports.Queries.GetAttendanceReport;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Reports;

/// <summary>
/// Cœur d'agrégation de l'assiduité, PARTAGÉ par le rapport paginé (ticket JGK-R02) et l'export de
/// fichier (ticket JGK-R03). Une seule source de vérité pour le calcul ET pour la garde de sécurité :
/// la validation « le classId appartient au tenant » vit ici, de sorte qu'aucun des deux chemins ne
/// puisse l'oublier.
///
/// Renvoie la liste COMPLÈTE des élèves (non paginée) : R02 en découpe une page, R03 l'exporte
/// entière. Le volume est borné (les élèves d'une école), la pagination en mémoire côté R02 est donc
/// sans conséquence.
/// </summary>
public class AttendanceReportAggregator(IApplicationDbContext dbContext)
{
    public async Task<AttendanceAggregate> ComputeAsync(
        DateOnly startDate, DateOnly endDate, Guid? classId, CancellationToken cancellationToken)
    {
        // Anti-falsification (exigence R02/R03) : un classId hors de l'école courante est refusé
        // EXPLICITEMENT en 422 — la RLS le masquerait déjà (rapport vide), mais on ne laisse pas croire
        // à une classe « sans données » là où l'identifiant est hors périmètre.
        if (classId is { } cid)
        {
            var classroomExists = await dbContext.Classrooms.AnyAsync(c => c.Id == cid, cancellationToken);
            if (!classroomExists)
            {
                throw new ValidationException([
                    new ValidationFailure("ClassId", "La classe indiquée n'existe pas dans votre établissement.")
                ]);
            }
        }

        // Lignes d'appel de la période, filtrées par classe si demandée. Aucun filtre SchoolId à la
        // main : le Global Query Filter + la policy RLS bornent déjà tout à l'école courante (règle #2).
        var lines =
            from sa in dbContext.StudentAttendances.AsNoTracking()
            join sheet in dbContext.AttendanceSheets.AsNoTracking() on sa.AttendanceSheetId equals sheet.Id
            where sheet.Date >= startDate
                  && sheet.Date <= endDate
                  && (classId == null || sheet.ClassroomId == classId)
            select new { sa.StudentId, sa.Status, sa.LateMinutes };

        var totalLines = await lines.CountAsync(cancellationToken);
        var presentLines = await lines
            .CountAsync(x => x.Status == AttendanceStatus.Present || x.Status == AttendanceStatus.Late, cancellationToken);

        decimal? averageRate = totalLines > 0
            ? Math.Round((decimal)presentLines / totalLines, 4)
            : null;

        var grouped =
            from x in lines
            join s in dbContext.Students.AsNoTracking() on x.StudentId equals s.Id
            group new { x.Status, x.LateMinutes } by new { s.Id, s.Matricule, s.FullName, s.ClassroomId } into g
            select new
            {
                g.Key.Id,
                g.Key.Matricule,
                g.Key.FullName,
                g.Key.ClassroomId,
                TotalCalls = g.Count(),
                Present = g.Sum(e => e.Status == AttendanceStatus.Present ? 1 : 0),
                Late = g.Sum(e => e.Status == AttendanceStatus.Late ? 1 : 0),
                Justified = g.Sum(e => e.Status == AttendanceStatus.JustifiedAbsence ? 1 : 0),
                Unjustified = g.Sum(e => e.Status == AttendanceStatus.UnjustifiedAbsence ? 1 : 0),
                LateMinutes = g.Sum(e => e.LateMinutes)
            };

        var rows = await grouped
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        var classroomIds = rows.Select(r => r.ClassroomId).Distinct().ToList();
        var classroomNames = await dbContext.Classrooms.AsNoTracking()
            .Where(c => classroomIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var students = rows
            .Select(r => new StudentAttendanceReportRow(
                r.Id,
                r.Matricule,
                r.FullName,
                r.ClassroomId,
                classroomNames.GetValueOrDefault(r.ClassroomId, "Classe supprimée"),
                r.TotalCalls,
                r.Present,
                r.Late,
                r.Justified,
                r.Unjustified,
                r.LateMinutes,
                r.TotalCalls > 0 ? Math.Round((decimal)(r.Present + r.Late) / r.TotalCalls, 4) : 0m))
            .ToList();

        return new AttendanceAggregate(averageRate, students);
    }
}

public record AttendanceAggregate(decimal? AverageAttendanceRate, IReadOnlyList<StudentAttendanceReportRow> Students);
