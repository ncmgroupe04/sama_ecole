using SamaEcole.Application.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Classrooms;

/// <summary>
/// Cycle d'enseignement d'une classe, DÉRIVÉ de son niveau (<see cref="Domain.Entities.Classroom.Level"/>).
///
/// Pourquoi une dérivation plutôt qu'un champ saisi à part : <c>Cycle</c> a été ajouté à l'entité sans
/// jamais être branché sur CreateClassroomCommand ni UpdateClassroomCommand. Il n'était donc écrit nulle
/// part et TOUTES les classes sont restées sur son défaut <c>College</c> — y compris les classes de
/// Primaire, dont le bulletin s'intitulait « COLLÈGE DE », les notes se saisissaient sur /20 au lieu de
/// /10 et la moyenne se pondérait par des coefficients que le primaire n'a pas. Deux champs à tenir
/// cohérents, c'est deux champs qui finissent par se contredire : le niveau, lui, est déjà choisi dans
/// une liste fermée à l'écran Classes, et il porte exactement la même information.
///
/// Le niveau reste un TEXTE LIBRE côté API (CreateClassroomCommandValidator n'impose aucune liste —
/// AGENTS.md : le Sénégal compte des établissements de toutes nomenclatures). D'où la reconnaissance
/// tolérante ci-dessous, insensible à la casse et aux accents, et un repli explicite.
/// </summary>
public static class ClassroomCycle
{
    /// <summary>
    /// Niveaux reconnus, pliés (majuscules sans accents). Les synonymes couvrent les nomenclatures
    /// réellement rencontrées au Sénégal — « Moyen » pour le collège (CEM = Collège d'Enseignement
    /// Moyen), « Élémentaire » pour le primaire, « Secondaire » pour le lycée.
    /// </summary>
    private static readonly Dictionary<string, CycleType> ByLevel = new()
    {
        ["CRECHE"] = CycleType.Maternelle,
        ["MATERNELLE"] = CycleType.Maternelle,
        ["PRESCOLAIRE"] = CycleType.Maternelle,

        ["PRIMAIRE"] = CycleType.Primaire,
        ["ELEMENTAIRE"] = CycleType.Primaire,
        ["ECOLE ELEMENTAIRE"] = CycleType.Primaire,
        ["ECOLE PRIMAIRE"] = CycleType.Primaire,

        ["COLLEGE"] = CycleType.College,
        ["MOYEN"] = CycleType.College,
        ["CEM"] = CycleType.College,

        ["LYCEE"] = CycleType.Lycee,
        ["SECONDAIRE"] = CycleType.Lycee
    };

    /// <summary>
    /// Cycle correspondant au niveau saisi. Un niveau NON reconnu retombe sur <see cref="CycleType.College"/> :
    /// c'est le comportement historique de l'entité, et le barème /20 est celui de la majorité des
    /// établissements — mais surtout, un repli sur un cycle à notation simplifiée ferait basculer en /10
    /// des notes déjà saisies sur /20, ce qui est bien plus destructeur que l'inverse.
    /// </summary>
    public static CycleType CycleFor(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return CycleType.College;
        }

        return ByLevel.GetValueOrDefault(TextFolding.Fold(level.Trim()), CycleType.College);
    }
}
