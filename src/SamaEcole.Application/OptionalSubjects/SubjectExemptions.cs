using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.OptionalSubjects;

/// <summary>Une matière dont un élève est dispensé. <see cref="IsMandatory"/> : la matière est obligatoire (dispense avec motif).</summary>
public sealed record ExemptSubject(Guid SubjectId, string Name, decimal Coefficient, bool IsMandatory);

/// <summary>
/// Les dispenses ACTIVES d'un élève pour une année. Deux sortes, traitées pareil par le calcul (la matière
/// n'entre plus dans les moyennes ni dans la saisie) et différemment par le bulletin (spec §4.4) :
/// — <see cref="HiddenIds"/> : options NON SUIVIES — la ligne n'est pas imprimée ;
/// — <see cref="Mandatory"/> : matières OBLIGATOIRES dispensées — la ligne reste, marquée « Dispensé(e) ».
/// <see cref="Contains"/> répond pour les deux sortes.
/// </summary>
public sealed class StudentExemptions
{
    public static StudentExemptions None { get; } = new([]);

    private readonly HashSet<Guid> _all;

    public StudentExemptions(IReadOnlyList<ExemptSubject> subjects)
    {
        Subjects = subjects;
        _all = subjects.Select(s => s.SubjectId).ToHashSet();
    }

    public IReadOnlyList<ExemptSubject> Subjects { get; }

    public bool IsEmpty => Subjects.Count == 0;

    public IReadOnlyList<ExemptSubject> Mandatory => Subjects.Where(s => s.IsMandatory).ToList();

    public IReadOnlySet<Guid> HiddenIds => Subjects.Where(s => !s.IsMandatory).Select(s => s.SubjectId).ToHashSet();

    public IReadOnlySet<Guid> MandatoryIds => Subjects.Where(s => s.IsMandatory).Select(s => s.SubjectId).ToHashSet();

    public bool Contains(Guid subjectId) => _all.Contains(subjectId);
}

/// <summary>
/// Requêtes de dispenses lues par les lecteurs de la spécification §4.2 (sommaire de notes, fiche élève,
/// structure APC, feuilles de notes, import, saisie). Classe STATIQUE et sans état : aucun paramètre de
/// constructeur à ajouter aux handlers, aucun cache à invalider (écart E1).
///
/// Règles tenues ICI, pour qu'aucun lecteur ne les réimplémente :
/// — l'inscription doit être ACTIVE pour l'année (hors <see cref="EnrollmentStatus.Cancelled"/>, même
///   convention que <c>CoefficientOverrideLoader</c>) ;
/// — la ligne est ACTIVE si la matière est encore optionnelle OU si la ligne porte un motif : repasser une
///   matière en « obligatoire » la rend à tous, sans purge, puisque ses lignes d'option n'ont pas de motif.
///
/// Aucun filtre SchoolId à la main : le Global Query Filter et la policy RLS bornent tout à l'école
/// courante (règle #2). Une dispense supprimée logiquement est déjà écartée par le filtre.
/// </summary>
public static class SubjectExemptions
{
    /// <summary>Les dispenses actives de l'élève pour cette année. Vide = il suit tout.</summary>
    public static async Task<StudentExemptions> ForStudentAsync(
        IApplicationDbContext dbContext, Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var rows = await (
            from x in dbContext.EnrollmentSubjectExemptions.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on x.EnrollmentId equals e.Id
            join s in dbContext.Subjects.AsNoTracking() on x.SubjectId equals s.Id
            where e.StudentId == studentId
                  && e.SchoolYearId == schoolYearId
                  && e.Status != EnrollmentStatus.Cancelled
                  && (s.IsOptional || x.Reason != null)
            select new { s.Id, s.Name, s.Coefficient, s.IsOptional })
            .ToListAsync(cancellationToken);

        return rows.Count == 0
            ? StudentExemptions.None
            : new StudentExemptions(rows
                .Select(r => new ExemptSubject(r.Id, r.Name, r.Coefficient, IsMandatory: !r.IsOptional))
                .ToList());
    }

    /// <summary>Les élèves dispensés de cette matière pour cette année, des deux sortes (feuilles de notes, import, saisie).</summary>
    public static async Task<IReadOnlySet<Guid>> StudentsExemptFromAsync(
        IApplicationDbContext dbContext, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        var ids = await (
            from x in dbContext.EnrollmentSubjectExemptions.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on x.EnrollmentId equals e.Id
            join s in dbContext.Subjects.AsNoTracking() on x.SubjectId equals s.Id
            where x.SubjectId == subjectId
                  && e.SchoolYearId == schoolYearId
                  && e.Status != EnrollmentStatus.Cancelled
                  && (s.IsOptional || x.Reason != null)
            select e.StudentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
