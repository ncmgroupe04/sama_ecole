using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Calcule ce qui empêche (ou accompagne) la suppression d'un élève (spec 2026-10-17, §2.3 et §6). Seul endroit qui
/// connaît la règle financière : un paiement NON ANNULÉ rattaché à l'une de ses inscriptions interdit la
/// suppression, sans option de rupture. Un paiement annulé ne bloque plus.
///
/// Lecture seule : ne modifie rien. Le Global Query Filter + la RLS bornent à l'école courante — un élève d'une autre
/// école est introuvable (404).
/// </summary>
public class StudentDeletionAnalyzer(IApplicationDbContext db)
{
    public async Task<DeletionImpact> AnalyzeAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var exists = await db.Students.AnyAsync(s => s.Id == studentId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException($"Élève {studentId} introuvable.");
        }

        var hasActivePayment = await (
            from p in db.Payments
            join e in db.Enrollments on p.EnrollmentId equals e.Id
            where e.StudentId == studentId && p.Status != PaymentStatus.Cancelled
            select p.Id)
            .AnyAsync(cancellationToken);

        if (hasActivePayment)
        {
            return new DeletionImpact(false, true, DeletionMessages.StudentFinancialBlock, []);
        }

        var links = new List<DeletionLink>();
        foreach (var dependency in StudentDependencyCatalog.All)
        {
            var count = await dependency.CountAsync(db, studentId, cancellationToken);
            if (count > 0)
            {
                links.Add(new DeletionLink(
                    dependency.Kind,
                    DeletionMessages.CountLabel(count, dependency.Singular, dependency.Plural),
                    count,
                    dependency.Breakable,
                    dependency.BreakAction));
            }
        }

        if (links.Count == 0)
        {
            return new DeletionImpact(true, false, null, links);
        }

        return new DeletionImpact(
            false, false, DeletionMessages.DependenciesBlock("Cet élève est actuellement rattaché", links), links);
    }
}
