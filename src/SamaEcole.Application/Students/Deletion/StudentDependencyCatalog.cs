using System.Linq.Expressions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Liste exhaustive des liens d'un élève (spec 2026-10-17, §6). Rompre un lien = l'ARCHIVER (suppression logique,
/// auteur et date conservés) : rien n'est effacé. Un test (StudentDependencyCoverageTests) garantit que toute table
/// qui référence un élève est ici, ou dans <see cref="IgnoredDependents"/>.
///
/// Ne figure PAS ici : les paiements (règle financière, traitée par StudentDeletionAnalyzer) et le rattachement à la
/// classe (Student.ClassroomId, obligatoire : il disparaît avec la fiche archivée).
/// </summary>
public static class StudentDependencyCatalog
{
    /// <summary>Tables qui référencent un élève mais n'empêchent ni ne font rompre rien : traces immuables.</summary>
    public static IReadOnlySet<Type> IgnoredDependents { get; } = new HashSet<Type>
    {
        typeof(SmsMessage) // journal des SMS envoyés : trace immuable, reste en base avec la fiche archivée
    };

    public static IReadOnlyList<StudentDependency> All { get; } =
    [
        Enrollments(),
        Owned<BoardingEnrollment>("BoardingEnrollment", "séjour d'internat", "séjours d'internat", "Libérer le lit et archiver le séjour",
            db => db.BoardingEnrollments, id => e => e.StudentId == id),
        Owned<StudentSubjectEnrollment>("SubjectEnrollment", "matière suivie", "matières suivies", "Retirer l'élève de ces matières",
            db => db.StudentSubjectEnrollments, id => e => e.StudentId == id),
        Owned<StudentSubjectExemption>("SubjectExemption", "dispense de matière", "dispenses de matière", "Retirer ces dispenses",
            db => db.StudentSubjectExemptions, id => e => e.StudentId == id),
        Owned<Grade>("Grade", "note", "notes", "Archiver ces notes",
            db => db.Grades, id => e => e.StudentId == id),
        Owned<ReportCardRemark>("ReportCardRemark", "remarque de bulletin", "remarques de bulletin", "Archiver ces remarques",
            db => db.ReportCardRemarks, id => e => e.StudentId == id),
        Owned<StudentAttendance>("Attendance", "pointage de présence", "pointages de présence", "Archiver ces pointages",
            db => db.StudentAttendances, id => e => e.StudentId == id),
        Owned<RollBookEntry>("RollBookEntry", "pointage du cahier de roulement", "pointages du cahier de roulement", "Archiver ces pointages",
            db => db.RollBookEntries, id => e => e.StudentId == id),
        Owned<AbsenceJustification>("AbsenceJustification", "justificatif d'absence", "justificatifs d'absence", "Archiver ces justificatifs",
            db => db.AbsenceJustifications, id => e => e.StudentId == id),
        Owned<LateArrival>("LateArrival", "retard", "retards", "Archiver ces retards",
            db => db.LateArrivals, id => e => e.StudentId == id),
        Owned<EarlyDeparture>("EarlyDeparture", "sortie anticipée", "sorties anticipées", "Archiver ces sorties",
            db => db.EarlyDepartures, id => e => e.StudentId == id),
        Owned<DisciplineRecord>("Discipline", "dossier disciplinaire", "dossiers disciplinaires", "Archiver ces dossiers",
            db => db.DisciplineRecords, id => e => e.StudentId == id),
        Owned<ParentSummons>("ParentSummons", "convocation de parent", "convocations de parent", "Archiver ces convocations",
            db => db.ParentSummons, id => e => e.StudentId == id),
        Owned<QuranProgress>("QuranProgress", "suivi coranique", "suivis coraniques", "Archiver ces suivis",
            db => db.QuranProgresses, id => e => e.StudentId == id),
        Owned<QuranEvaluation>("QuranEvaluation", "évaluation coranique", "évaluations coraniques", "Archiver ces évaluations",
            db => db.QuranEvaluations, id => e => e.StudentId == id),
        Owned<StudentHizbStatus>("HizbStatus", "statut de Hizb", "statuts de Hizb", "Archiver ces statuts",
            db => db.StudentHizbStatuses, id => e => e.StudentId == id),
        Owned<StudentHizbEvaluation>("HizbEvaluation", "évaluation de Hizb", "évaluations de Hizb", "Archiver ces évaluations",
            db => db.StudentHizbEvaluations, id => e => e.StudentId == id),
        Owned<ExamDossier>("ExamDossier", "dossier d'examen", "dossiers d'examen", "Archiver ces dossiers",
            db => db.ExamDossiers, id => e => e.StudentId == id),
        Owned<StudentMutationCertificate>("MutationCertificate", "attestation de mutation", "attestations de mutation", "Archiver ces attestations",
            db => db.StudentMutationCertificates, id => e => e.StudentId == id),
        ReturnedLoans(),
        UnreturnedLoans()
    ];

    private static StudentDependency Owned<T>(
        string kind, string singular, string plural, string breakAction,
        Func<IApplicationDbContext, DbSet<T>> set,
        Func<Guid, Expression<Func<T, bool>>> forStudent) where T : AuditableEntity =>
        new(kind, singular, plural, breakAction, Breakable: true, typeof(T),
            CountAsync: (db, studentId, ct) => set(db).CountAsync(forStudent(studentId), ct),
            BreakAsync: (db, studentId, actor, ct) => ArchiveAsync(set(db).Where(forStudent(studentId)), actor, ct));

    private static async Task<int> ArchiveAsync<T>(IQueryable<T> query, string actor, CancellationToken ct)
        where T : AuditableEntity
    {
        var rows = await query.ToListAsync(ct);
        foreach (var row in rows)
        {
            row.SoftDelete(actor);
        }
        return rows.Count;
    }

    /// <summary>
    /// Inscription : la rompre l'archive avec ses enfants (lignes de frais, échéanciers, engagements, relances). Les
    /// paiements ne sont jamais touchés ici : s'il en reste un non annulé, l'analyseur a déjà bloqué la suppression.
    /// </summary>
    private static StudentDependency Enrollments() => new(
        "Enrollment", "inscription", "inscriptions", "Annuler l'inscription et l'archiver", Breakable: true, typeof(Enrollment),
        CountAsync: (db, studentId, ct) => db.Enrollments.CountAsync(e => e.StudentId == studentId, ct),
        BreakAsync: async (db, studentId, actor, ct) =>
        {
            var enrollments = await db.Enrollments.Where(e => e.StudentId == studentId).ToListAsync(ct);
            var ids = enrollments.Select(e => e.Id).ToList();

            await ArchiveAsync(db.EnrollmentFeeLines.Where(l => ids.Contains(l.EnrollmentId)), actor, ct);
            await ArchiveAsync(db.FinancialCommitments.Where(c => ids.Contains(c.EnrollmentId)), actor, ct);
            await ArchiveAsync(db.DebtorReminderBatchItems.Where(i => ids.Contains(i.EnrollmentId)), actor, ct);

            var plans = await db.FeeInstallmentPlans.Where(p => ids.Contains(p.EnrollmentId)).ToListAsync(ct);
            var planIds = plans.Select(p => p.Id).ToList();
            await ArchiveAsync(db.FeeInstallments.Where(i => planIds.Contains(i.FeeInstallmentPlanId)), actor, ct);
            foreach (var plan in plans)
            {
                plan.SoftDelete(actor);
            }

            foreach (var enrollment in enrollments)
            {
                enrollment.Status = EnrollmentStatus.Cancelled;
                enrollment.SoftDelete(actor);
            }

            return enrollments.Count;
        });

    /// <summary>Matériel déjà restitué ou déclaré perdu : de l'historique, archivé avec l'élève.</summary>
    private static StudentDependency ReturnedLoans() => Owned<ItemAssignment>(
        "ReturnedLoan", "prêt de matériel clos", "prêts de matériel clos", "Archiver ces prêts",
        db => db.ItemAssignments,
        id => a => a.StudentId == id && (a.Status == AssignmentStatus.Restitue || a.Status == AssignmentStatus.Perdu));

    /// <summary>
    /// Matériel encore chez l'élève : le stock en dépend (mouvements append-only), on ne l'archive jamais en silence.
    /// Non rompable — l'utilisateur enregistre d'abord la restitution depuis l'Inventaire.
    /// </summary>
    private static StudentDependency UnreturnedLoans() => new(
        "UnreturnedLoan", "matériel prêté non restitué", "matériels prêtés non restitués", string.Empty,
        Breakable: false, typeof(ItemAssignment),
        CountAsync: (db, studentId, ct) => db.ItemAssignments.CountAsync(
            a => a.StudentId == studentId
                 && (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue), ct),
        BreakAsync: (_, _, _, _) => Task.FromResult(0));
}
