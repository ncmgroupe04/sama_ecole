using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Deletion;

/// <summary>
/// Règle de suppression d'une année scolaire en MODE RÉEL (spec 2026-10-17, §6) :
///   • un paiement NON ANNULÉ sur une inscription de l'année bloque, sans option de rupture ;
///   • tout le reste de ce que l'année porte (inscriptions, notes, appréciations de bulletin, feuilles d'appel, sessions
///     d'examen, certificats de mutation, suivi et dispenses de matières, coefficients personnalisés) est un lien listé,
///     rompu explicitement par archivage — avant, une année qui portait la moindre donnée était refusée sans issue ;
///   • la configuration de l'année (trimestres, affectations d'enseignants, jours de fermeture) est intrinsèque : archivée
///     avec elle et annoncée à l'écran.
/// En MODE TEST, l'exception bornée historique (effacement définitif par <c>delete_school_year</c>) est inchangée : elle
/// n'utilise pas cette politique. Un test (DeletionDependencyCoverageTests) garantit que toute table liée est classée ici.
/// </summary>
public static class SchoolYearDeletionPolicy
{
    public const string FinancialBlock =
        "Impossible de supprimer cette année scolaire car des transactions financières / reçus enregistrés y sont rattachés.";

    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Enrollments(id => e => e.SchoolYearId == id),
        TermGrades(),
        TermReportCardRemarks(),
        DeletionDependencies.AttendanceSheets(id => s => s.SchoolYearId == id),
        ExamSessions(),
        DeletionDependencies.Owned<StudentMutationCertificate>("MutationCertificate", "certificat de mutation", "certificats de mutation",
            "Archiver ces certificats", db => db.StudentMutationCertificates, id => c => c.SchoolYearId == id),
        DeletionDependencies.Owned<StudentSubjectEnrollment>("SubjectEnrollment", "suivi de matière par un élève", "suivis de matières par des élèves",
            "Retirer ces suivis", db => db.StudentSubjectEnrollments, id => e => e.SchoolYearId == id),
        DeletionDependencies.Owned<StudentSubjectExemption>("SubjectExemption", "dispense de matière", "dispenses de matières",
            "Archiver ces dispenses", db => db.StudentSubjectExemptions, id => e => e.SchoolYearId == id),
        DeletionDependencies.Owned<SubjectCoefficientOverride>("CoefficientOverride", "coefficient personnalisé", "coefficients personnalisés",
            "Archiver ces coefficients", db => db.SubjectCoefficientOverrides, id => o => o.SchoolYearId == id)
    ];

    /// <summary>
    /// Configuration de l'année, créée avec elle (les trimestres le sont automatiquement) : archivée avec l'année, pas
    /// listée comme lien à rompre. Doit passer APRÈS la rupture des notes et appréciations qui s'y rattachent.
    /// </summary>
    public static IReadOnlyList<DeletionDependency> Intrinsic { get; } =
    [
        DeletionDependencies.Owned<Term>("Term", "trimestre", "trimestres",
            "Archiver ces trimestres", db => db.Terms, id => t => t.SchoolYearId == id),
        DeletionDependencies.Owned<TeacherAssignment>("TeacherAssignment", "affectation d'enseignant", "affectations d'enseignants",
            "Retirer ces affectations", db => db.TeacherAssignments, id => a => a.SchoolYearId == id),
        DeletionDependencies.Owned<SchoolClosure>("SchoolClosure", "jour de fermeture", "jours de fermeture",
            "Archiver ces jours de fermeture", db => db.SchoolClosures, id => c => c.SchoolYearId == id)
    ];

    public static EntityDeletionPolicy Policy { get; } = new(
        "SchoolYear",
        id => $"Année scolaire {id} introuvable.",
        "Cette année scolaire est actuellement rattachée",
        (db, id, ct) => db.SchoolYears.AnyAsync(y => y.Id == id, ct),
        async (db, id, ct) =>
        {
            var hasActivePayment = await (
                from p in db.Payments
                join e in db.Enrollments on p.EnrollmentId equals e.Id
                where e.SchoolYearId == id && p.Status != PaymentStatus.Cancelled
                select p.Id)
                .AnyAsync(ct);

            return hasActivePayment ? FinancialBlock : null;
        },
        Dependencies,
        Intrinsic);

    /// <summary>Vrai en mode test (établissement pas encore en production) : l'effacement définitif historique s'applique.</summary>
    public static async Task<bool> IsTestModeAsync(IApplicationDbContext db, Guid schoolId, CancellationToken ct)
    {
        var wentLiveAt = await db.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.WentLiveAt)
            .FirstOrDefaultAsync(ct);

        return wentLiveAt is null;
    }

    /// <summary>
    /// Analyse selon le régime : en mode test l'effacement définitif emporte tout (paiements compris), l'analyse ne liste
    /// donc rien ; en mode réel c'est l'analyse commune.
    /// </summary>
    public static async Task<DeletionImpact> AnalyzeAsync(
        IApplicationDbContext db, Guid schoolId, Guid yearId, CancellationToken ct)
    {
        if (!await IsTestModeAsync(db, schoolId, ct))
        {
            return await EntityDeletionAnalyzer.AnalyzeAsync(db, Policy, yearId, ct);
        }

        if (!await Policy.ExistsAsync(db, yearId, ct))
        {
            throw new KeyNotFoundException(Policy.NotFoundMessage(yearId));
        }

        return new DeletionImpact(true, false, null, []);
    }

    private static Task<List<Guid>> TermIdsAsync(IApplicationDbContext db, Guid yearId, CancellationToken ct) =>
        db.Terms.Where(t => t.SchoolYearId == yearId).Select(t => t.Id).ToListAsync(ct);

    /// <summary>Notes saisies dans les trimestres de l'année (rattachées par TermId, sans navigation).</summary>
    private static DeletionDependency TermGrades() => new(
        "Grade", "note", "notes", "Archiver ces notes", Breakable: true, typeof(Grade),
        CountAsync: async (db, id, ct) =>
        {
            var termIds = await TermIdsAsync(db, id, ct);
            return await db.Grades.CountAsync(g => termIds.Contains(g.TermId), ct);
        },
        BreakAsync: async (db, id, actor, ct) =>
        {
            var termIds = await TermIdsAsync(db, id, ct);
            return await DeletionDependencies.ArchiveAsync(db.Grades.Where(g => termIds.Contains(g.TermId)), actor, ct);
        });

    private static DeletionDependency TermReportCardRemarks() => new(
        "ReportCardRemark", "appréciation de bulletin", "appréciations de bulletin", "Archiver ces appréciations",
        Breakable: true, typeof(ReportCardRemark),
        CountAsync: async (db, id, ct) =>
        {
            var termIds = await TermIdsAsync(db, id, ct);
            return await db.ReportCardRemarks.CountAsync(r => termIds.Contains(r.TermId), ct);
        },
        BreakAsync: async (db, id, actor, ct) =>
        {
            var termIds = await TermIdsAsync(db, id, ct);
            return await DeletionDependencies.ArchiveAsync(db.ReportCardRemarks.Where(r => termIds.Contains(r.TermId)), actor, ct);
        });

    /// <summary>Sessions d'examen de l'année, avec leurs dossiers de candidature et leurs résultats.</summary>
    private static DeletionDependency ExamSessions() => new(
        "ExamSession", "session d'examen", "sessions d'examen", "Archiver ces sessions avec leurs dossiers et résultats",
        Breakable: true, typeof(ExamSession),
        CountAsync: (db, id, ct) => db.ExamSessions.CountAsync(s => s.SchoolYearId == id, ct),
        BreakAsync: async (db, id, actor, ct) =>
        {
            var sessions = await db.ExamSessions.Where(s => s.SchoolYearId == id).ToListAsync(ct);
            var sessionIds = sessions.Select(s => s.Id).ToList();

            var dossiers = await db.ExamDossiers.Where(d => sessionIds.Contains(d.ExamSessionId)).ToListAsync(ct);
            var dossierIds = dossiers.Select(d => d.Id).ToList();

            await DeletionDependencies.ArchiveAsync(db.ExamResults.Where(r => dossierIds.Contains(r.ExamDossierId)), actor, ct);

            foreach (var dossier in dossiers)
            {
                dossier.SoftDelete(actor);
            }

            foreach (var session in sessions)
            {
                session.SoftDelete(actor);
            }

            return sessions.Count;
        });
}
