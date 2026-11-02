using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Rompt TOUT ce qui est rattaché à des inscriptions qu'on annule, pour qu'il n'en reste aucune trace active — quel que soit
/// le profil de l'école (général, franco-arabe, internat…) : le séjour d'internat (lit libéré, puis archivé) avec ses sorties et
/// ses pointages d'appel, les lignes de frais, les échéanciers négociés et leurs échéances, les engagements financiers et les
/// lignes de relance. Rien n'est effacé (AGENTS.md règle #6) : tout est archivé, auteur et date conservés.
///
/// Les paiements ne sont jamais touchés ici : l'appelant a déjà refusé l'annulation s'il en existe un non annulé. L'inscription
/// elle-même reste, au statut « Annulée » (le numéro de reçu officiel est sans trou) — c'est à l'appelant de la positionner.
/// Aucun <c>SaveChanges</c> : l'appelant décide de la transaction.
/// </summary>
public static class EnrollmentLinkRelease
{
    public static async Task ArchiveAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> enrollmentIds, string actor, CancellationToken cancellationToken)
    {
        await ArchiveBoardingAsync(db, enrollmentIds, actor, cancellationToken);

        await DeletionDependencies.ArchiveAsync(
            db.EnrollmentFeeLines.Where(l => enrollmentIds.Contains(l.EnrollmentId)), actor, cancellationToken);
        await DeletionDependencies.ArchiveAsync(
            db.FinancialCommitments.Where(c => enrollmentIds.Contains(c.EnrollmentId)), actor, cancellationToken);
        await DeletionDependencies.ArchiveAsync(
            db.DebtorReminderBatchItems.Where(i => enrollmentIds.Contains(i.EnrollmentId)), actor, cancellationToken);

        var plans = await db.FeeInstallmentPlans.Where(p => enrollmentIds.Contains(p.EnrollmentId)).ToListAsync(cancellationToken);
        var planIds = plans.Select(p => p.Id).ToList();
        await DeletionDependencies.ArchiveAsync(
            db.FeeInstallments.Where(i => planIds.Contains(i.FeeInstallmentPlanId)), actor, cancellationToken);
        foreach (var plan in plans)
        {
            plan.SoftDelete(actor);
        }
    }

    /// <summary>
    /// Séjours d'internat des inscriptions (actifs ou déjà terminés), leurs sorties et leurs pointages. Un séjour actif est
    /// d'abord clos et son lit libéré (contraintes <c>inactive_no_bed</c> et <c>ended_has_end</c>) : sans cela, annuler
    /// l'inscription laissait un lit « fantôme » occupé par un élève qui n'est plus inscrit.
    /// </summary>
    private static async Task ArchiveBoardingAsync(
        IApplicationDbContext db, IReadOnlyCollection<Guid> enrollmentIds, string actor, CancellationToken cancellationToken)
    {
        var stays = await db.BoardingEnrollments
            .Where(b => enrollmentIds.Contains(b.EnrollmentId))
            .ToListAsync(cancellationToken);

        if (stays.Count == 0)
        {
            return;
        }

        var stayIds = stays.Select(s => s.Id).ToList();

        await DeletionDependencies.ArchiveAsync(
            db.BoardingLeaves.Where(l => stayIds.Contains(l.BoardingEnrollmentId)), actor, cancellationToken);
        await DeletionDependencies.ArchiveAsync(
            db.BoardingAttendances.Where(a => a.BoardingEnrollmentId != null && stayIds.Contains(a.BoardingEnrollmentId.Value)),
            actor, cancellationToken);

        foreach (var stay in stays)
        {
            stay.IsActive = false;
            stay.BedId = null;
            stay.EndDate ??= stay.StartDate;
            stay.SoftDelete(actor);
        }
    }
}
