using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subjects.Deletion;

/// <summary>
/// Règle de suppression d'une matière (spec 2026-10-17, §6). Aucune donnée financière. Les liens se rompent
/// explicitement par archivage — y compris les NOTES déjà saisies (le nombre est affiché avant confirmation, elles ne
/// figureront plus dans les moyennes ni les bulletins). Les activités d'un domaine, elles, ne se rompent pas en un clic :
/// les archiver en cascade ferait disparaître des notes sans que l'école l'ait décidé ligne par ligne (comportement
/// historique conservé). Un test (SubjectDependencyCoverageTests) garantit que toute table liée à une matière est classée.
/// </summary>
public static class SubjectDeletionPolicy
{
    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Blocking<Subject>("ChildSubject", "activité d'évaluation", "activités d'évaluation",
            "Supprimez-les ou rattachez-les à un autre domaine, une par une",
            db => db.Subjects, id => s => s.ParentSubjectId == id),
        DeletionDependencies.Owned<Grade>("Grade", "note", "notes",
            "Archiver ces notes (elles ne figureront plus dans les moyennes ni les bulletins)",
            db => db.Grades, id => g => g.SubjectId == id),
        DeletionDependencies.ClassSubjects("classe où elle est enseignée", "classes où elle est enseignée",
            "Retirer cette matière de ces classes", id => c => c.SubjectId == id),
        DeletionDependencies.Owned<StudentSubjectExemption>("SubjectExemption", "dispense de matière", "dispenses de matière",
            "Retirer ces dispenses", db => db.StudentSubjectExemptions, id => e => e.SubjectId == id),
        DeletionDependencies.Owned<SubjectCoefficientOverride>("CoefficientOverride", "coefficient personnalisé", "coefficients personnalisés",
            "Archiver ces coefficients", db => db.SubjectCoefficientOverrides, id => o => o.SubjectId == id),
        DeletionDependencies.Owned<TeacherAssignment>("TeacherAssignment", "affectation d'enseignant", "affectations d'enseignants",
            "Retirer ces affectations", db => db.TeacherAssignments, id => a => a.SubjectId == id),
        DeletionDependencies.Owned<TeacherSubject>("TeacherSubject", "enseignant qualifié", "enseignants qualifiés",
            "Retirer ces rattachements", db => db.TeacherSubjects, id => t => t.SubjectId == id),
        DeletionDependencies.Owned<ScheduleSlot>("ScheduleSlot", "créneau d'emploi du temps", "créneaux d'emploi du temps",
            "Archiver ces créneaux", db => db.ScheduleSlots, id => s => s.SubjectId == id),
        DeletionDependencies.Owned<ClassJournalEntry>("ClassJournalEntry", "entrée du cahier de texte", "entrées du cahier de texte",
            "Archiver ces entrées", db => db.ClassJournalEntries, id => e => e.SubjectId == id),
        DeletionDependencies.AttendanceSheets(id => s => s.SubjectId == id),
        DeletionDependencies.Owned<SyllabusUnit>("SyllabusUnit", "unité de programme", "unités de programme",
            "Archiver ces unités", db => db.SyllabusUnits, id => u => u.SubjectId == id),
        DeletionDependencies.Owned<WeeklyHourNorm>("WeeklyHourNorm", "norme d'heures hebdomadaires", "normes d'heures hebdomadaires",
            "Archiver ces normes", db => db.WeeklyHourNorms, id => n => n.SubjectId == id)
    ];

    public static EntityDeletionPolicy Policy { get; } = new(
        "Subject",
        id => $"Matière {id} introuvable.",
        "Cette matière est actuellement rattachée",
        (db, id, ct) => db.Subjects.AnyAsync(s => s.Id == id, ct),
        (_, _, _) => Task.FromResult<string?>(null),
        Dependencies);
}
