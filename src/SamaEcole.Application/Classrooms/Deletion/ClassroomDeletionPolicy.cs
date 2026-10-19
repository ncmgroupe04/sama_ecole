using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Classrooms.Deletion;

/// <summary>
/// Règle de suppression d'une classe (spec 2026-10-17, §6) :
///   • un paiement NON ANNULÉ sur une inscription de la classe bloque, sans option de rupture (propagation du blocage
///     financier de l'élève à la classe) ;
///   • les élèves rattachés ne se « détachent » pas (Student.ClassroomId est obligatoire) : lien NON rompable ici — on
///     les transfère vers une autre classe ou on supprime leur fiche ;
///   • tout le reste (inscriptions, matières de la classe, affectations, emploi du temps, frais, cahier de texte, feuilles
///     d'appel, dossiers d'examen, coefficients, relances) se rompt explicitement par archivage.
/// Un test (ClassroomDependencyCoverageTests) garantit que toute table qui référence une classe est classée ici.
/// </summary>
public static class ClassroomDeletionPolicy
{
    public const string FinancialBlock =
        "Impossible de supprimer cette classe car des transactions financières / reçus enregistrés y sont rattachés.";

    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Blocking<Student>("Student", "élève", "élèves",
            "Transférez ces élèves vers une autre classe ou supprimez leur fiche",
            db => db.Students, id => s => s.ClassroomId == id),
        DeletionDependencies.Enrollments(id => e => e.ClassroomId == id),
        DeletionDependencies.ClassSubjects("matière de la classe", "matières de la classe", "Retirer ces matières de la classe",
            id => c => c.ClassroomId == id),
        DeletionDependencies.Owned<TeacherAssignment>("TeacherAssignment", "affectation d'enseignant", "affectations d'enseignants",
            "Retirer ces affectations", db => db.TeacherAssignments, id => a => a.ClassroomId == id),
        DeletionDependencies.Owned<ScheduleSlot>("ScheduleSlot", "créneau d'emploi du temps", "créneaux d'emploi du temps",
            "Archiver ces créneaux", db => db.ScheduleSlots, id => s => s.ClassroomId == id),
        DeletionDependencies.Owned<ClassFee>("ClassFee", "frais de classe", "frais de classe",
            "Archiver ces frais", db => db.ClassFees, id => f => f.ClassroomId == id),
        DeletionDependencies.Owned<ClassJournalEntry>("ClassJournalEntry", "entrée du cahier de texte", "entrées du cahier de texte",
            "Archiver ces entrées", db => db.ClassJournalEntries, id => e => e.ClassroomId == id),
        DeletionDependencies.AttendanceSheets(id => s => s.ClassroomId == id),
        DeletionDependencies.Owned<RollBookEntry>("RollBookEntry", "pointage du cahier de roulement", "pointages du cahier de roulement",
            "Archiver ces pointages", db => db.RollBookEntries, id => e => e.ClassroomId == id),
        DeletionDependencies.Owned<ExamDossier>("ExamDossier", "dossier d'examen", "dossiers d'examen",
            "Archiver ces dossiers", db => db.ExamDossiers, id => d => d.ClassroomId == id),
        DeletionDependencies.Owned<SubjectCoefficientOverride>("CoefficientOverride", "coefficient personnalisé", "coefficients personnalisés",
            "Archiver ces coefficients", db => db.SubjectCoefficientOverrides, id => o => o.ClassroomId == id),
        ReminderBatches()
    ];

    public static EntityDeletionPolicy Policy { get; } = new(
        "Classroom",
        id => $"Classe {id} introuvable.",
        "Cette classe est actuellement rattachée",
        (db, id, ct) => db.Classrooms.AnyAsync(c => c.Id == id, ct),
        async (db, id, ct) =>
        {
            var hasActivePayment = await (
                from p in db.Payments
                join e in db.Enrollments on p.EnrollmentId equals e.Id
                where e.ClassroomId == id && p.Status != PaymentStatus.Cancelled
                select p.Id)
                .AnyAsync(ct);

            return hasActivePayment ? FinancialBlock : null;
        },
        Dependencies);

    /// <summary>Lots de relances d'impayés de la classe, avec leurs lignes.</summary>
    private static DeletionDependency ReminderBatches() => new(
        "ReminderBatch", "lot de relances d'impayés", "lots de relances d'impayés", "Archiver ces lots",
        Breakable: true, typeof(DebtorReminderBatch),
        CountAsync: (db, id, ct) => db.DebtorReminderBatches.CountAsync(b => b.ClassroomId == id, ct),
        BreakAsync: async (db, id, actor, ct) =>
        {
            var batches = await db.DebtorReminderBatches.Where(b => b.ClassroomId == id).ToListAsync(ct);
            var ids = batches.Select(b => b.Id).ToList();

            await DeletionDependencies.ArchiveAsync(
                db.DebtorReminderBatchItems.Where(i => ids.Contains(i.DebtorReminderBatchId)), actor, ct);

            foreach (var batch in batches)
            {
                batch.SoftDelete(actor);
            }

            return batches.Count;
        });
}
