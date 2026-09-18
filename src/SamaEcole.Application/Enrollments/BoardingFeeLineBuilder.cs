using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Construit les lignes de frais d'internat MANQUANTES d'une inscription (module Internat) — même
/// calcul que CreateEnrollmentCommandHandler.BuildFeeLinesAsync (montants COPIÉS depuis ClassFee,
/// mensualité × tuitionMonths), restreint aux catégories FeeCategory.IsBoardingFee et EXCLUANT
/// celles déjà présentes sur l'inscription (<paramref name="existingFeeCategoryIds"/>) : c'est ce
/// filtre qui garantit qu'un second changement de chambre (ChangeBoardingAssignmentCommand) ne
/// double-facture jamais la pension. Partagé par CreateEnrollmentCommandHandler (aucune ligne
/// existante — élève Externe/nouvel Interne) et ChangeBoardingAssignmentCommandHandler (l'inscription
/// existe déjà, ses lignes actuelles bornent ce qui reste à ajouter).
/// </summary>
public static class BoardingFeeLineBuilder
{
    public static async Task<List<EnrollmentFeeLine>> BuildMissingBoardingLinesAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        Guid classroomId,
        int tuitionMonths,
        IReadOnlySet<Guid> existingFeeCategoryIds,
        CancellationToken cancellationToken)
    {
        var boardingFees = await (
            from fee in dbContext.ClassFees
            join category in dbContext.FeeCategories on fee.FeeCategoryId equals category.Id
            where fee.ClassroomId == classroomId && category.IsBoardingFee
            select new { fee.FeeCategoryId, category.Name, category.IsRecurring, fee.Amount })
            .ToListAsync(cancellationToken);

        return boardingFees
            .Where(f => !existingFeeCategoryIds.Contains(f.FeeCategoryId))
            .Select(f =>
            {
                var months = f.IsRecurring ? tuitionMonths : 1;
                return new EnrollmentFeeLine
                {
                    SchoolId = schoolId,
                    FeeCategoryId = f.FeeCategoryId,
                    Designation = f.Name,
                    IsRecurring = f.IsRecurring,
                    UnitAmount = f.Amount,
                    Months = months,
                    LineTotal = f.Amount * months
                };
            })
            .ToList();
    }
}
