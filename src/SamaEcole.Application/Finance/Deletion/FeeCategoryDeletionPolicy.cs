using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Deletion;

/// <summary>
/// Règle de suppression d'une catégorie de frais (spec 2026-10-17, §6) :
///   • BLOQUÉE (donnée financière, sans option de rupture) si une inscription la facture (lignes de frais dus figées à
///     l'inscription) ou si un paiement NON ANNULÉ l'a ventilée ;
///   • les lignes du barème (ClassFee) se rompent explicitement par archivage — l'ancien comportement les archivait en
///     cascade, sans le dire.
/// Un test (DeletionDependencyCoverageTests) garantit que toute table liée à une catégorie est classée ici.
/// </summary>
public static class FeeCategoryDeletionPolicy
{
    public const string FinancialBlock =
        "Impossible de supprimer cette catégorie de frais car des inscriptions ou des paiements enregistrés l'utilisent.";

    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Owned<ClassFee>("ClassFee", "ligne du barème", "lignes du barème",
            "Archiver ces lignes du barème", db => db.ClassFees, id => f => f.FeeCategoryId == id)
    ];

    /// <summary>Tables qui référencent une catégorie et sont couvertes par la règle financière ci-dessous (pas par un lien rompable).</summary>
    public static IReadOnlySet<Type> CoveredByFinancialRule { get; } = new HashSet<Type>
    {
        typeof(EnrollmentFeeLine),
        typeof(PaymentBreakdown)
    };

    public static EntityDeletionPolicy Policy { get; } = new(
        "FeeCategory",
        id => $"Catégorie de frais {id} introuvable.",
        "Cette catégorie de frais est actuellement rattachée",
        (db, id, ct) => db.FeeCategories.AnyAsync(c => c.Id == id, ct),
        async (db, id, ct) =>
        {
            var billed = await db.EnrollmentFeeLines.AnyAsync(l => l.FeeCategoryId == id, ct);
            if (billed)
            {
                return FinancialBlock;
            }

            var paid = await (
                from b in db.PaymentBreakdowns
                join p in db.Payments on b.PaymentId equals p.Id
                where b.FeeCategoryId == id && p.Status != PaymentStatus.Cancelled
                select b.Id)
                .AnyAsync(ct);

            return paid ? FinancialBlock : null;
        },
        Dependencies);
}
