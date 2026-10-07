using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding;

/// <summary>
/// Occupation d'un lit : PROJETÉE depuis les séjours actifs, jamais stockée (spec N2). Une seule définition de
/// « lit occupé » pour toutes les lectures et gardes de suppression.
/// </summary>
public static class BoardingOccupancy
{
    /// <summary>Identifiants des lits tenus par un séjour actif (le Global Query Filter écarte déjà les supprimés).</summary>
    public static IQueryable<Guid> ActiveBedIds(IApplicationDbContext dbContext) =>
        dbContext.BoardingEnrollments
            .Where(b => b.IsActive && b.BedId != null)
            .Select(b => b.BedId!.Value);

    public static BedStatus StatusOf(BedStatus stored, bool hasActiveStay) =>
        stored == BedStatus.Maintenance ? BedStatus.Maintenance
        : hasActiveStay ? BedStatus.Occupied
        : BedStatus.Available;

    /// <summary>Taux d'occupation (0..1) sur les lits UTILISABLES (hors maintenance).</summary>
    public static decimal Rate(int occupied, int capacity, int maintenance)
    {
        var usable = capacity - maintenance;
        return usable <= 0 ? 0m : Math.Round((decimal)occupied / usable, 4);
    }
}
