using SamaEcole.Application.Common.Deletion;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Buildings.Deletion;

/// <summary>
/// Règle de suppression d'un bâtiment (spec 2026-10-17, §6) : aucune donnée financière ; les salles rattachées sont un
/// lien NON rompable ici — on supprime ou déplace chaque salle d'abord, une par une (comportement historique conservé :
/// archiver un bâtiment en cascade ferait disparaître des salles que l'école n'a pas décidé de retirer). Un test
/// (DeletionDependencyCoverageTests) garantit que toute table liée à un bâtiment est classée ici.
/// </summary>
public static class BuildingDeletionPolicy
{
    public static IReadOnlyList<DeletionDependency> Dependencies { get; } =
    [
        DeletionDependencies.Blocking<Room>("Room", "salle", "salles",
            "Supprimez ou déplacez ces salles d'abord, une par une",
            db => db.Rooms, id => r => r.BuildingId == id)
    ];

    public static EntityDeletionPolicy Policy { get; } = new(
        "Building",
        id => $"Bâtiment {id} introuvable.",
        "Ce bâtiment est actuellement rattaché",
        (db, id, ct) => db.Buildings.AnyAsync(b => b.Id == id, ct),
        (_, _, _) => Task.FromResult<string?>(null),
        Dependencies);
}
