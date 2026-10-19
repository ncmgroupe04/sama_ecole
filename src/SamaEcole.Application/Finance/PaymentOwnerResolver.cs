using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance;

/// <summary>L'élève auquel appartient un versement, même si sa fiche ou son inscription ont été supprimées depuis.</summary>
public sealed record PaymentOwner(Guid EnrollmentId, Guid StudentId, string FullName, string Matricule, bool StudentDeleted)
{
    /// <summary>Nom à imprimer dans un état : « Awa Fall (élève supprimé) » quand la fiche est archivée.</summary>
    public string DisplayName => StudentDeleted ? $"{FullName} (élève supprimé)" : FullName;
}

/// <summary>
/// Retrouve l'élève d'un versement pour les états financiers (journaux de caisse, reçu). Un élève supprimé laisse des
/// paiements ANNULÉS conservés en base (preuve comptable) : leur historique doit rester lisible. L'inscription et
/// l'élève, filtrés une fois archivés, sont donc lus avec <c>IgnoreQueryFilters()</c> — qui retire AUSSI le filtre de
/// tenant : le <c>SchoolId</c> est rétabli à la main (la RLS PostgreSQL protège en dernier recours).
/// </summary>
public static class PaymentOwnerResolver
{
    public static async Task<Dictionary<Guid, PaymentOwner>> ResolveAsync(
        IApplicationDbContext dbContext, Guid schoolId, IReadOnlyCollection<Guid> enrollmentIds,
        CancellationToken cancellationToken) =>
        await (
            from e in dbContext.Enrollments.IgnoreQueryFilters().AsNoTracking()
            join s in dbContext.Students.IgnoreQueryFilters().AsNoTracking() on e.StudentId equals s.Id
            where e.SchoolId == schoolId && s.SchoolId == schoolId && enrollmentIds.Contains(e.Id)
            select new PaymentOwner(e.Id, s.Id, s.FullName, s.Matricule, s.IsDeleted))
            .ToDictionaryAsync(o => o.EnrollmentId, cancellationToken);
}
