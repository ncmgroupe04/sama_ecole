using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Liste exhaustive des liens d'un élève (spec 2026-10-17, §6). Un test (StudentDependencyCoverageTests) garantit que
/// toute table qui référence un élève est ici, ou dans <see cref="IgnoredDependents"/>.
///
/// Ne figure PAS ici : les paiements (règle financière, voir <see cref="StudentDeletionPolicy"/>) et le rattachement à la
/// classe (Student.ClassroomId, obligatoire : il disparaît avec la fiche archivée).
/// </summary>
public static class StudentDependencyCatalog
{
    /// <summary>Tables qui référencent un élève mais n'empêchent ni ne font rompre rien : traces immuables.</summary>
    public static IReadOnlySet<Type> IgnoredDependents { get; } = new HashSet<Type>
    {
        typeof(SmsMessage) // journal des SMS envoyés : trace immuable, reste en base avec la fiche archivée
    };

    public static IReadOnlyList<DeletionDependency> All { get; } =
    [
        DeletionDependencies.Enrollments(id => e => e.StudentId == id),
        DeletionDependencies.Owned<BoardingEnrollment>("BoardingEnrollment", "séjour d'internat", "séjours d'internat", "Libérer le lit et archiver le séjour",
            db => db.BoardingEnrollments, id => e => e.StudentId == id),
        DeletionDependencies.Owned<StudentSubjectEnrollment>("SubjectEnrollment", "matière suivie", "matières suivies", "Retirer l'élève de ces matières",
            db => db.StudentSubjectEnrollments, id => e => e.StudentId == id),
        DeletionDependencies.Owned<StudentSubjectExemption>("SubjectExemption", "dispense de matière", "dispenses de matière", "Retirer ces dispenses",
            db => db.StudentSubjectExemptions, id => e => e.StudentId == id),
        DeletionDependencies.Owned<Grade>("Grade", "note", "notes", "Archiver ces notes",
            db => db.Grades, id => e => e.StudentId == id),
        DeletionDependencies.Owned<ReportCardRemark>("ReportCardRemark", "remarque de bulletin", "remarques de bulletin", "Archiver ces remarques",
            db => db.ReportCardRemarks, id => e => e.StudentId == id),
        DeletionDependencies.Owned<StudentAttendance>("Attendance", "pointage de présence", "pointages de présence", "Archiver ces pointages",
            db => db.StudentAttendances, id => e => e.StudentId == id),
        DeletionDependencies.Owned<AbsenceJustification>("AbsenceJustification", "justificatif d'absence", "justificatifs d'absence", "Archiver ces justificatifs",
            db => db.AbsenceJustifications, id => e => e.StudentId == id),
        DeletionDependencies.Owned<LateArrival>("LateArrival", "retard", "retards", "Archiver ces retards",
            db => db.LateArrivals, id => e => e.StudentId == id),
        DeletionDependencies.Owned<EarlyDeparture>("EarlyDeparture", "sortie anticipée", "sorties anticipées", "Archiver ces sorties",
            db => db.EarlyDepartures, id => e => e.StudentId == id),
        DeletionDependencies.Owned<DisciplineRecord>("Discipline", "dossier disciplinaire", "dossiers disciplinaires", "Archiver ces dossiers",
            db => db.DisciplineRecords, id => e => e.StudentId == id),
        DeletionDependencies.Owned<ParentSummons>("ParentSummons", "convocation de parent", "convocations de parent", "Archiver ces convocations",
            db => db.ParentSummons, id => e => e.StudentId == id),
        DeletionDependencies.Owned<QuranProgress>("QuranProgress", "suivi coranique", "suivis coraniques", "Archiver ces suivis",
            db => db.QuranProgresses, id => e => e.StudentId == id),
        DeletionDependencies.Owned<QuranEvaluation>("QuranEvaluation", "évaluation coranique", "évaluations coraniques", "Archiver ces évaluations",
            db => db.QuranEvaluations, id => e => e.StudentId == id),
        DeletionDependencies.Owned<StudentHizbStatus>("HizbStatus", "statut de Hizb", "statuts de Hizb", "Archiver ces statuts",
            db => db.StudentHizbStatuses, id => e => e.StudentId == id),
        DeletionDependencies.Owned<StudentHizbEvaluation>("HizbEvaluation", "évaluation de Hizb", "évaluations de Hizb", "Archiver ces évaluations",
            db => db.StudentHizbEvaluations, id => e => e.StudentId == id),
        DeletionDependencies.Owned<ExamDossier>("ExamDossier", "dossier d'examen", "dossiers d'examen", "Archiver ces dossiers",
            db => db.ExamDossiers, id => e => e.StudentId == id),
        DeletionDependencies.Owned<StudentMutationCertificate>("MutationCertificate", "attestation de mutation", "attestations de mutation", "Archiver ces attestations",
            db => db.StudentMutationCertificates, id => e => e.StudentId == id),
        DeletionDependencies.ReturnedLoans(id => a => a.StudentId == id),
        DeletionDependencies.UnreturnedLoans(id => a => a.StudentId == id)
    ];
}

/// <summary>Règle de suppression d'un élève : un paiement non annulé bloque, sans option de rupture (spec §2.3).</summary>
public static class StudentDeletionPolicy
{
    public static EntityDeletionPolicy Policy { get; } = new(
        "Student",
        id => $"Élève {id} introuvable.",
        "Cet élève est actuellement rattaché",
        (db, id, ct) => db.Students.AnyAsync(s => s.Id == id, ct),
        async (db, id, ct) =>
        {
            var hasActivePayment = await (
                from p in db.Payments
                join e in db.Enrollments on p.EnrollmentId equals e.Id
                where e.StudentId == id && p.Status != PaymentStatus.Cancelled
                select p.Id)
                .AnyAsync(ct);

            return hasActivePayment ? DeletionMessages.StudentFinancialBlock : null;
        },
        StudentDependencyCatalog.All);
}
