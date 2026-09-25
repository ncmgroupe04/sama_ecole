using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exemptions;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects;

/// <summary>
/// Applique <see cref="SubjectFollowRules"/> aux données (Évolution N°6) : QUI suit QUELLE matière, pour une
/// année scolaire. Seul point d'entrée des lecteurs — grille de saisie, import, fiche papier, bulletin, fiche
/// élève — pour qu'une matière optionnelle soit masquée partout ou nulle part.
///
/// Classe de référence de l'élève : <see cref="StudentYearClassroom"/>, la même que pour les coefficients.
/// Mémoïsé pour la durée du scope (un bulletin de classe interroge élève par élève). Aucun filtre SchoolId à la
/// main : le Global Query Filter et la policy RLS bornent tout à l'école courante (règle #2).
/// </summary>
public class SubjectFollowScope(IApplicationDbContext dbContext)
{
    private static readonly IReadOnlySet<Guid> NoExclusion = new HashSet<Guid>();

    private readonly Dictionary<(Guid Student, Guid Year), IReadOnlySet<Guid>> _excluded = [];
    private readonly Dictionary<(Guid Student, Guid Year), IReadOnlyList<ExemptSubject>> _exemptions = [];

    /// <summary>
    /// Matières dont l'élève est DISPENSÉ cette année (dispense d'une matière obligatoire, avec motif). Elles sortent
    /// des moyennes et de la saisie comme une option non suivie ; le bulletin, lui, les garde, marquées. Mémoïsé.
    /// Le motif n'est jamais porté ici : c'est une donnée sensible.
    /// </summary>
    public async Task<IReadOnlyList<ExemptSubject>> ExemptionsAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        if (_exemptions.TryGetValue((studentId, schoolYearId), out var cached))
        {
            return cached;
        }

        var result = await ExemptionQueries.ForStudentAsync(dbContext, studentId, schoolYearId, cancellationToken);
        _exemptions[(studentId, schoolYearId)] = result;
        return result;
    }

    /// <summary>Matières que l'élève ne suit pas cette année-là (vide pour une classe non configurée).</summary>
    public async Task<IReadOnlySet<Guid>> ExcludedSubjectsAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        if (_excluded.TryGetValue((studentId, schoolYearId), out var cached))
        {
            return cached;
        }

        var result = await ResolveExcludedAsync(studentId, schoolYearId, cancellationToken);

        // Dispenses d'une matière obligatoire : une SECONDE source d'exclusion, réunie ici pour que tous les lecteurs
        // (résumé, fiche, bulletins, saisie) l'appliquent sans la connaître.
        var exemptions = await ExemptionsAsync(studentId, schoolYearId, cancellationToken);
        if (exemptions.Count > 0)
        {
            result = result.Union(exemptions.Select(e => e.SubjectId)).ToHashSet();
        }

        _excluded[(studentId, schoolYearId)] = result;
        return result;
    }

    private async Task<IReadOnlySet<Guid>> ResolveExcludedAsync(
        Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, studentId, schoolYearId, cancellationToken);
        if (classroomId is null)
        {
            return NoExclusion;
        }

        var classSubjects = await dbContext.ClassSubjects.AsNoTracking()
            .Where(c => c.ClassroomId == classroomId)
            .Select(c => new ClassSubjectRule(c.Id, c.SubjectId, c.IsActive, c.OptionGroup))
            .ToListAsync(cancellationToken);

        if (classSubjects.Count == 0)
        {
            return NoExclusion;
        }

        var chosen = (await dbContext.StudentSubjectEnrollments.AsNoTracking()
                .Where(e => e.StudentId == studentId && e.SchoolYearId == schoolYearId)
                .Select(e => e.ClassSubjectId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return SubjectFollowRules.ExcludedSubjects(classSubjects, chosen);
    }

    /// <summary>
    /// Élèves de la classe autorisés sur une matière : <c>null</c> si toute la classe la suit (matière commune
    /// ou hors programme configuré), l'ensemble — éventuellement vide — des élèves qui l'ont choisie pour une
    /// matière optionnelle, et un ensemble vide pour une matière désactivée.
    /// </summary>
    public async Task<IReadOnlySet<Guid>?> RestrictedStudentsAsync(
        Guid classroomId, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var restricted = await ResolveRestrictedAsync(classroomId, subjectId, schoolYearId, cancellationToken);

        // Élèves dispensés de la matière : ils sortent de la grille, de l'import et des fiches. Sans dispense,
        // le résultat est celui d'avant (null = toute la classe).
        var exempt = await ExemptionQueries.StudentsAsync(dbContext, subjectId, schoolYearId, cancellationToken);
        if (exempt.Count == 0 || restricted is { Count: 0 })
        {
            return restricted;
        }

        if (restricted is not null)
        {
            return restricted.Where(id => !exempt.Contains(id)).ToHashSet();
        }

        var classStudents = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == classroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        return classStudents.Where(id => !exempt.Contains(id)).ToHashSet();
    }

    private async Task<IReadOnlySet<Guid>?> ResolveRestrictedAsync(
        Guid classroomId, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var classSubject = await dbContext.ClassSubjects.AsNoTracking()
            .Where(c => c.ClassroomId == classroomId && c.SubjectId == subjectId)
            .Select(c => new { c.Id, c.IsActive, c.OptionGroup })
            .FirstOrDefaultAsync(cancellationToken);

        if (classSubject is null)
        {
            return null;
        }

        if (!classSubject.IsActive)
        {
            return NoExclusion;
        }

        if (classSubject.OptionGroup is null)
        {
            return null;
        }

        return (await dbContext.StudentSubjectEnrollments.AsNoTracking()
                .Where(e => e.ClassSubjectId == classSubject.Id && e.SchoolYearId == schoolYearId)
                .Select(e => e.StudentId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    /// <summary>Année scolaire d'une période, ou null si elle n'existe pas dans l'école courante.</summary>
    public Task<Guid?> SchoolYearOfTermAsync(Guid termId, CancellationToken cancellationToken)
        => dbContext.Terms.AsNoTracking()
            .Where(t => t.Id == termId)
            .Select(t => (Guid?)t.SchoolYearId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Garde d'écriture d'une note : refuse (422) une note sur une matière que l'élève ne suit pas — une LV2 qu'il
    /// n'a pas choisie, une matière désactivée. Sans elle, la note serait invisible sur le bulletin.
    /// </summary>
    public async Task EnsureFollowsAsync(
        Guid studentId, Guid subjectId, Guid termId, string field, CancellationToken cancellationToken)
    {
        var yearId = await SchoolYearOfTermAsync(termId, cancellationToken);
        if (yearId is null)
        {
            return;
        }

        var exemptions = await ExemptionsAsync(studentId, yearId.Value, cancellationToken);
        if (exemptions.Any(e => e.SubjectId == subjectId))
        {
            throw new ValidationException([
                new ValidationFailure(field,
                    "Cet élève est dispensé de cette matière : aucune note ne peut y être saisie. "
                    + "Retirez la dispense sur sa fiche pour saisir une note.")
            ]);
        }

        var excluded = await ExcludedSubjectsAsync(studentId, yearId.Value, cancellationToken);
        if (excluded.Contains(subjectId))
        {
            throw new ValidationException([
                new ValidationFailure(field,
                    "Cet élève ne suit pas cette matière : elle est optionnelle et il ne l'a pas choisie, ou elle est "
                    + "désactivée pour sa classe. Corrigez ses options sur sa fiche avant de saisir la note.")
            ]);
        }
    }
}
