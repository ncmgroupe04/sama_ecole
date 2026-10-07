using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Assignments;

/// <summary>
/// Enregistrement qui traduit la course sur le dernier lit. L'index unique partiel
/// <c>UX_boarding_enrollments_active_bed</c> (lot A) est la garantie dure « un lit, un occupant actif » : si deux
/// affectations passent le pré-contrôle en même temps, la seconde échoue ici en 409 <c>BED_UNAVAILABLE</c>, jamais en 500.
/// </summary>
public static class BoardingConflicts
{
    public const string BedUnavailable = "BED_UNAVAILABLE";

    public static async Task SaveAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex)
            when (ex.TechnicalDetail.Contains("UX_boarding_enrollments_active_bed", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException("Ce lit vient d'être attribué à un autre pensionnaire.", BedUnavailable);
        }
    }
}
