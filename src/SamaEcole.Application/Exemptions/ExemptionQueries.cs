using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Exemptions;

/// <summary>
/// Requêtes de dispenses, STATIQUES et sans état : appelées par <c>SubjectFollowScope</c> (qui les mémoïse), par
/// <c>ReportCardDataService</c> et par les handlers de l'API — aucun paramètre de constructeur ajouté à un service
/// existant. Aucun filtre SchoolId à la main : le Global Query Filter et la policy RLS bornent tout à l'école
/// courante (règle #2) ; une dispense supprimée logiquement est déjà écartée.
/// </summary>
public static class ExemptionQueries
{
    /// <summary>Les matières dont l'élève est dispensé pour cette année, triées par nom (comme le résumé de notes).</summary>
    public static async Task<IReadOnlyList<ExemptSubject>> ForStudentAsync(
        IApplicationDbContext dbContext, Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var rows = await (
            from x in dbContext.StudentSubjectExemptions.AsNoTracking()
            join s in dbContext.Subjects.AsNoTracking() on x.SubjectId equals s.Id
            where x.StudentId == studentId && x.SchoolYearId == schoolYearId
            select new { s.Id, s.Name, s.Coefficient })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(r => r.Name)
            .Select(r => new ExemptSubject(r.Id, r.Name, r.Coefficient))
            .ToList();
    }

    /// <summary>Les élèves dispensés de cette matière pour cette année (feuilles de notes, import).</summary>
    public static async Task<IReadOnlySet<Guid>> StudentsAsync(
        IApplicationDbContext dbContext, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var ids = await dbContext.StudentSubjectExemptions.AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.SchoolYearId == schoolYearId)
            .Select(x => x.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    /// <summary>
    /// Matières dispensables d'une classe : OBLIGATOIRES et AUTONOMES (ni activité d'un domaine, ni domaine — une
    /// matière qui porte des activités n'est jamais notée). Classe AVEC programme (Évolution N°6) : ses
    /// <c>class_subjects</c> actifs sans groupe d'options. Classe SANS programme : les matières du niveau de la classe
    /// (texte libre, comparé en mémoire sans casse ni espaces de bord). Triées par nom.
    /// </summary>
    public static async Task<IReadOnlyList<DispensableSubject>> DispensableAsync(
        IApplicationDbContext dbContext, Guid classroomId, CancellationToken cancellationToken)
    {
        var program = await dbContext.ClassSubjects.AsNoTracking()
            .Where(c => c.ClassroomId == classroomId)
            .Select(c => new { c.SubjectId, c.IsActive, c.OptionGroup })
            .ToListAsync(cancellationToken);

        var autonomous = dbContext.Subjects.AsNoTracking()
            .Where(s => s.ParentSubjectId == null && !dbContext.Subjects.Any(child => child.ParentSubjectId == s.Id));

        if (program.Count > 0)
        {
            var ids = program.Where(c => c.IsActive && c.OptionGroup == null).Select(c => c.SubjectId).ToList();
            var byProgram = await autonomous
                .Where(s => ids.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(cancellationToken);

            return byProgram.OrderBy(s => s.Name).Select(s => new DispensableSubject(s.Id, s.Name)).ToList();
        }

        var level = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => c.Level)
            .FirstOrDefaultAsync(cancellationToken);

        if (level is null)
        {
            return [];
        }

        var byLevel = (await autonomous.Select(s => new { s.Id, s.Name, s.Level }).ToListAsync(cancellationToken))
            .Where(s => ExemptionRules.LevelMatches(s.Level, level))
            .OrderBy(s => s.Name)
            .Select(s => new DispensableSubject(s.Id, s.Name))
            .ToList();

        return byLevel;
    }
}
