using SamaEcole.Application.Common.Deletion;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Students.Deletion;

/// <summary>
/// Calcule ce qui empêche (ou accompagne) la suppression d'un élève (spec 2026-10-17, §2.3 et §6) : un paiement NON
/// ANNULÉ interdit la suppression sans option de rupture ; sinon la liste précise des liens. Lecture seule. Mince
/// façade sur le moteur commun <see cref="EntityDeletionAnalyzer"/> et la politique <see cref="StudentDeletionPolicy"/>.
/// </summary>
public class StudentDeletionAnalyzer(IApplicationDbContext db)
{
    public Task<DeletionImpact> AnalyzeAsync(Guid studentId, CancellationToken cancellationToken) =>
        EntityDeletionAnalyzer.AnalyzeAsync(db, StudentDeletionPolicy.Policy, studentId, cancellationToken);
}
