using System.Linq.Expressions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Common.Deletion;

/// <summary>
/// Briques communes pour décrire les liens d'un élément à supprimer. Rompre un lien = l'ARCHIVER (suppression logique,
/// auteur et date conservés) : rien n'est effacé (AGENTS.md règle #6).
/// </summary>
public static class DeletionDependencies
{
    /// <summary>Lien rompable : les lignes de <typeparamref name="T"/> qui désignent l'élément sont archivées.</summary>
    public static DeletionDependency Owned<T>(
        string kind, string singular, string plural, string breakAction,
        Func<IApplicationDbContext, DbSet<T>> set,
        Func<Guid, Expression<Func<T, bool>>> forOwner) where T : AuditableEntity =>
        new(kind, singular, plural, breakAction, Breakable: true, typeof(T),
            CountAsync: (db, ownerId, ct) => set(db).CountAsync(forOwner(ownerId), ct),
            BreakAsync: (db, ownerId, actor, ct) => ArchiveAsync(set(db).Where(forOwner(ownerId)), actor, ct));

    /// <summary>Lien qui bloque et ne se rompt pas ici : l'utilisateur doit d'abord faire <paramref name="beforeAction"/>.</summary>
    public static DeletionDependency Blocking<T>(
        string kind, string singular, string plural, string beforeAction,
        Func<IApplicationDbContext, DbSet<T>> set,
        Func<Guid, Expression<Func<T, bool>>> forOwner) where T : AuditableEntity =>
        new(kind, singular, plural, beforeAction, Breakable: false, typeof(T),
            CountAsync: (db, ownerId, ct) => set(db).CountAsync(forOwner(ownerId), ct),
            BreakAsync: (_, _, _, _) => Task.FromResult(0));

    public static async Task<int> ArchiveAsync<T>(IQueryable<T> query, string actor, CancellationToken ct)
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
    /// Inscriptions de l'élément (élève ou classe) : les rompre les annule et les archive avec leurs enfants (lignes de
    /// frais, échéanciers, engagements, relances). Les paiements ne sont jamais touchés ici : s'il en reste un non
    /// annulé, l'analyseur a déjà bloqué la suppression.
    /// </summary>
    public static DeletionDependency Enrollments(Func<Guid, Expression<Func<Enrollment, bool>>> forOwner) => new(
        "Enrollment", "inscription", "inscriptions", "Annuler l'inscription et l'archiver", Breakable: true, typeof(Enrollment),
        CountAsync: (db, ownerId, ct) => db.Enrollments.CountAsync(forOwner(ownerId), ct),
        BreakAsync: async (db, ownerId, actor, ct) =>
        {
            var enrollments = await db.Enrollments.Where(forOwner(ownerId)).ToListAsync(ct);
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

    /// <summary>Matières d'une classe (lignes ClassSubject) : les retirer retire aussi le suivi de ces matières par les élèves.</summary>
    public static DeletionDependency ClassSubjects(
        string singular, string plural, string breakAction, Func<Guid, Expression<Func<ClassSubject, bool>>> forOwner) => new(
        "ClassSubject", singular, plural, breakAction, Breakable: true, typeof(ClassSubject),
        CountAsync: (db, ownerId, ct) => db.ClassSubjects.CountAsync(forOwner(ownerId), ct),
        BreakAsync: async (db, ownerId, actor, ct) =>
        {
            var classSubjects = await db.ClassSubjects.Where(forOwner(ownerId)).ToListAsync(ct);
            var ids = classSubjects.Select(c => c.Id).ToList();

            await ArchiveAsync(db.StudentSubjectEnrollments.Where(e => ids.Contains(e.ClassSubjectId)), actor, ct);

            foreach (var classSubject in classSubjects)
            {
                classSubject.SoftDelete(actor);
            }

            return classSubjects.Count;
        });

    /// <summary>Feuilles d'appel, avec les pointages de présence qu'elles portent.</summary>
    public static DeletionDependency AttendanceSheets(Func<Guid, Expression<Func<AttendanceSheet, bool>>> forOwner) => new(
        "AttendanceSheet", "feuille d'appel", "feuilles d'appel", "Archiver ces feuilles d'appel",
        Breakable: true, typeof(AttendanceSheet),
        CountAsync: (db, ownerId, ct) => db.AttendanceSheets.CountAsync(forOwner(ownerId), ct),
        BreakAsync: async (db, ownerId, actor, ct) =>
        {
            var sheets = await db.AttendanceSheets.Where(forOwner(ownerId)).ToListAsync(ct);
            var ids = sheets.Select(s => s.Id).ToList();

            await ArchiveAsync(db.StudentAttendances.Where(a => ids.Contains(a.AttendanceSheetId)), actor, ct);

            foreach (var sheet in sheets)
            {
                sheet.SoftDelete(actor);
            }

            return sheets.Count;
        });

    /// <summary>Matériel déjà restitué ou déclaré perdu : de l'historique, archivé avec le bénéficiaire.</summary>
    public static DeletionDependency ReturnedLoans(Func<Guid, Expression<Func<ItemAssignment, bool>>> forBeneficiary) => Owned(
        "ReturnedLoan", "prêt de matériel clos", "prêts de matériel clos", "Archiver ces prêts",
        db => db.ItemAssignments,
        id => Combine(forBeneficiary(id), a => a.Status == AssignmentStatus.Restitue || a.Status == AssignmentStatus.Perdu));

    /// <summary>
    /// Matériel encore chez le bénéficiaire : le stock en dépend (mouvements append-only), on ne l'archive jamais en
    /// silence. Non rompable — l'utilisateur enregistre d'abord la restitution depuis l'Inventaire.
    /// </summary>
    public static DeletionDependency UnreturnedLoans(Func<Guid, Expression<Func<ItemAssignment, bool>>> forBeneficiary) => Blocking(
        "UnreturnedLoan", "matériel prêté non restitué", "matériels prêtés non restitués",
        "Enregistrez d'abord la restitution depuis l'Inventaire",
        db => db.ItemAssignments,
        id => Combine(forBeneficiary(id),
            a => a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue));

    private static Expression<Func<T, bool>> Combine<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var body = Expression.AndAlso(
            new ParameterReplacer(parameter).Visit(left.Body)!,
            new ParameterReplacer(parameter).Visit(right.Body)!);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression parameter) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => parameter;
    }
}
