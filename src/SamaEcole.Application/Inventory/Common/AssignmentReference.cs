using System.Globalization;

namespace SamaEcole.Application.Inventory.Common;

/// <summary>
/// Numéro de référence imprimé sur une fiche de décharge.
///
/// DÉRIVÉ de l'identifiant de la fiche, et non tiré d'un compteur : contrairement au matricule d'un
/// élève (AGENTS.md règle #3), une décharge n'a aucune exigence de numérotation continue — personne
/// ne réclame « la décharge n° 42 ». Un compteur imposerait ici une séquence en base, un verrou et
/// des trous à expliquer, pour un identifiant qui ne sert qu'à retrouver la fiche à partir du papier.
///
/// Déterministe : la même fiche réimprimée dix ans plus tard porte le même numéro que l'exemplaire
/// signé — c'est la seule propriété dont la décharge ait réellement besoin.
/// </summary>
public static class AssignmentReference
{
    public static string For(Guid assignmentId, DateOnly assignedOn) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"DEC-{assignedOn.Year}-{assignmentId.ToString("N")[..6].ToUpperInvariant()}");
}
