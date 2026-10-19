using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Deletion;

/// <summary>
/// Règle de suppression d'une catégorie d'inventaire (spec 2026-10-17, §6) : aucune donnée financière ; les biens
/// rattachés sont un lien NON rompable ici (ils portent chacun un journal de stock) — on les supprime ou on change leur
/// catégorie d'abord. Un test (DeletionDependencyCoverageTests) garantit que toute table liée est classée.
/// </summary>
public static class InventoryCategoryDeletionPolicy
{
    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Blocking<InventoryItem>("InventoryItem", "bien", "biens",
            "Supprimez ces biens ou changez leur catégorie d'abord",
            db => db.InventoryItems, id => i => i.CategoryId == id)
    ];

    public static EntityDeletionPolicy Policy { get; } = new(
        "InventoryCategory",
        id => $"Catégorie d'inventaire {id} introuvable.",
        "Cette catégorie est actuellement rattachée",
        (db, id, ct) => db.InventoryCategories.AnyAsync(c => c.Id == id, ct),
        (_, _, _) => Task.FromResult<string?>(null),
        Dependencies);
}

/// <summary>
/// Règle de suppression d'un bien d'inventaire (spec 2026-10-17, §6) :
///   • un prêt NON RESTITUÉ est un lien non rompable — la décharge signée n'aurait plus de bien en face ; on enregistre
///     d'abord la restitution ;
///   • les prêts CLOS (restitués ou déclarés perdus) sont de l'historique du bien : archivés avec lui (annoncé à l'écran) ;
///   • le journal de stock (StockMovement) est append-only : trace immuable qui reste consultable après l'archivage
///     du bien (comportement historique conservé).
/// </summary>
public static class InventoryItemDeletionPolicy
{
    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Blocking<ItemAssignment>("OpenLoan", "prêt en cours", "prêts en cours",
            "Enregistrez d'abord la restitution depuis l'Inventaire",
            db => db.ItemAssignments,
            id => a => a.ItemId == id
                       && (a.Status == AssignmentStatus.EnCours || a.Status == AssignmentStatus.PartiellementRestitue))
    ];

    public static IReadOnlyList<DeletionDependency> Intrinsic { get; } =
    [
        DeletionDependencies.ReturnedLoans(id => a => a.ItemId == id)
    ];

    /// <summary>Traces immuables qui référencent un bien et ne s'archivent jamais (append-only).</summary>
    public static IReadOnlySet<Type> IgnoredDependents { get; } = new HashSet<Type> { typeof(StockMovement) };

    public static EntityDeletionPolicy Policy { get; } = new(
        "InventoryItem",
        id => $"Bien d'inventaire {id} introuvable.",
        "Ce bien est actuellement rattaché",
        (db, id, ct) => db.InventoryItems.AnyAsync(i => i.Id == id, ct),
        (_, _, _) => Task.FromResult<string?>(null),
        Dependencies,
        Intrinsic);
}
