using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Deletion;

/// <summary>
/// Règle de suppression d'un enseignant (spec 2026-10-17, §6) :
///   • une FICHE DE PAIE émise sur l'un de ses contrats bloque, sans option de rupture (donnée financière) ;
///   • son contrat (sans fiche de paie), ses affectations, matières, créneaux, pointages et journaux se rompent
///     explicitement par archivage ;
///   • le matériel prêté non restitué n'est pas rompable (le stock en dépend) : on enregistre d'abord la restitution.
/// Un test (TeacherDependencyCoverageTests) garantit que toute table liée à un enseignant est classée ici.
/// </summary>
public static class TeacherDeletionPolicy
{
    public const string FinancialBlock =
        "Impossible de supprimer cet enseignant car il possède des fiches de paie / transactions financières enregistrées.";

    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        Contracts(),
        DeletionDependencies.Owned<TeacherAssignment>("TeacherAssignment", "affectation classe/matière", "affectations classe/matière",
            "Retirer ces affectations", db => db.TeacherAssignments, id => a => a.TeacherId == id),
        DeletionDependencies.Owned<TeacherSubject>("TeacherSubject", "matière enseignée", "matières enseignées",
            "Retirer ces rattachements", db => db.TeacherSubjects, id => t => t.TeacherId == id),
        DeletionDependencies.Owned<ScheduleSlot>("ScheduleSlot", "créneau d'emploi du temps", "créneaux d'emploi du temps",
            "Archiver ces créneaux", db => db.ScheduleSlots, id => s => s.TeacherId == id),
        DeletionDependencies.Owned<TeacherAttendance>("TeacherAttendance", "pointage d'enseignant", "pointages d'enseignant",
            "Archiver ces pointages", db => db.TeacherAttendances, id => a => a.TeacherId == id),
        DeletionDependencies.Owned<ClassJournalEntry>("ClassJournalEntry", "entrée du cahier de texte", "entrées du cahier de texte",
            "Archiver ces entrées", db => db.ClassJournalEntries, id => e => e.TeacherId == id),
        DeletionDependencies.ReturnedLoans(id => a => a.TeacherId == id),
        DeletionDependencies.UnreturnedLoans(id => a => a.TeacherId == id)
    ];

    public static EntityDeletionPolicy Policy { get; } = new(
        "Teacher",
        id => $"Enseignant {id} introuvable.",
        "Cet enseignant est actuellement rattaché",
        (db, id, ct) => db.Teachers.AnyAsync(t => t.Id == id, ct),
        async (db, id, ct) =>
        {
            // Les contrats archivés restent lus (IgnoreQueryFilters) : une fiche de paie émise ne se perd pas de vue parce que
            // son contrat a été archivé. Seules les fiches de l'école courante sont visibles (filtre de tenant du paiement).
            var hasPayslip = await (
                from f in db.FichePaies
                join c in db.EmployeeContracts.IgnoreQueryFilters() on f.EmployeeContractId equals c.Id
                where c.TeacherId == id
                select f.Id)
                .AnyAsync(ct);

            return hasPayslip ? FinancialBlock : null;
        },
        Dependencies);

    /// <summary>Contrat de l'enseignant (sans fiche de paie : sinon le blocage financier a déjà joué), avec son historique et ses relevés d'heures.</summary>
    private static DeletionDependency Contracts() => new(
        "EmployeeContract", "contrat de travail", "contrats de travail", "Archiver ce contrat avec son historique et ses relevés d'heures",
        Breakable: true, typeof(EmployeeContract),
        CountAsync: (db, id, ct) => db.EmployeeContracts.CountAsync(c => c.TeacherId == id, ct),
        BreakAsync: async (db, id, actor, ct) =>
        {
            var contracts = await db.EmployeeContracts.Where(c => c.TeacherId == id).ToListAsync(ct);
            var ids = contracts.Select(c => c.Id).ToList();

            await DeletionDependencies.ArchiveAsync(
                db.EmployeeContractHistories.Where(h => ids.Contains(h.EmployeeContractId)), actor, ct);
            await DeletionDependencies.ArchiveAsync(
                db.TeacherHourRecords.Where(r => ids.Contains(r.EmployeeContractId)), actor, ct);

            foreach (var contract in contracts)
            {
                contract.SoftDelete(actor);
            }

            return contracts.Count;
        });
}
