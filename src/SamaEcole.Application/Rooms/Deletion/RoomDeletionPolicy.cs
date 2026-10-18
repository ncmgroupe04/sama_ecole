using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Rooms.Deletion;

/// <summary>
/// Règle de suppression d'une salle (spec 2026-10-17, §6) : aucune donnée financière. Les biens d'inventaire qui y sont
/// rangés sont DÉTACHÉS (leur emplacement est vidé) — rien n'est archivé ni supprimé côté inventaire. Un test
/// (DeletionDependencyCoverageTests) garantit que toute table liée à une salle est classée ici.
/// </summary>
public static class RoomDeletionPolicy
{
    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Detach<InventoryItem>("InventoryItem", "bien rangé dans cette salle", "biens rangés dans cette salle",
            "Retirer l'emplacement de ces biens (ils ne sont pas supprimés)",
            db => db.InventoryItems, id => i => i.RoomId == id, i => i.RoomId = null)
    ];

    /// <summary>
    /// Enrollment porte une colonne RoomId « conservée pour alignement avec le schéma » (lot F2) : propriété fantôme, sans
    /// propriété CLR, jamais renseignée par l'application — aucune inscription ne peut donc pointer une salle.
    /// </summary>
    public static IReadOnlySet<Type> IgnoredDependents { get; } = new HashSet<Type> { typeof(Enrollment) };

    public static EntityDeletionPolicy Policy { get; } = new(
        "Room",
        id => $"Salle {id} introuvable.",
        "Cette salle est actuellement rattachée",
        (db, id, ct) => db.Rooms.AnyAsync(r => r.Id == id, ct),
        (_, _, _) => Task.FromResult<string?>(null),
        Dependencies);
}
