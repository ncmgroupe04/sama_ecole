using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Charge les surcharges de coefficient applicables à un élève pour une année (Évolution N°4).
///
/// Classe de référence : celle de l'INSCRIPTION de l'année — l'historique ne suit pas les changements de
/// classe : un élève passé de S2 à L2 garde, pour l'année précédente, les coefficients de S2 — et, à défaut
/// d'inscription, sa classe actuelle (arbitrage A11).
///
/// Mémoïsé pour la durée du scope : un bulletin de classe appelle le résumé de notes élève par élève, on ne
/// relit pas trente fois les mêmes lignes. Aucun filtre SchoolId à la main : le Global Query Filter et la
/// policy RLS bornent tout à l'école courante (règle #2).
/// </summary>
public class CoefficientOverrideLoader(IApplicationDbContext dbContext)
{
    private readonly Dictionary<(Guid Student, Guid Year), CoefficientOverrides> _cache = [];

    public async Task<CoefficientOverrides> LoadAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue((studentId, schoolYearId), out var cached))
        {
            return cached;
        }

        var result = await ResolveAsync(studentId, schoolYearId, cancellationToken);
        _cache[(studentId, schoolYearId)] = result;
        return result;
    }

    private async Task<CoefficientOverrides> ResolveAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, studentId, schoolYearId, cancellationToken);

        if (classroomId is null)
        {
            return CoefficientOverrides.None;
        }

        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => new { c.Id, c.Series })
            .FirstOrDefaultAsync(cancellationToken);

        if (classroom is null)
        {
            return CoefficientOverrides.None;
        }

        var classroomKey = classroom.Id;
        var series = classroom.Series;

        // UNE requête : les lignes de la classe ET celles de sa série (si elle en a une), pour l'année.
        var rows = await dbContext.SubjectCoefficientOverrides.AsNoTracking()
            .Where(o => o.SchoolYearId == schoolYearId
                        && (o.ClassroomId == classroomKey || (series != null && o.Series == series)))
            .Select(o => new { o.SubjectId, o.ClassroomId, o.Coefficient })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return CoefficientOverrides.None;
        }

        return new CoefficientOverrides(
            rows.Where(r => r.ClassroomId != null).ToDictionary(r => r.SubjectId, r => r.Coefficient),
            rows.Where(r => r.ClassroomId == null).ToDictionary(r => r.SubjectId, r => r.Coefficient));
    }
}
