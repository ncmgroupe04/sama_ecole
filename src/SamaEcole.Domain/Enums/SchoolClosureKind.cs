namespace SamaEcole.Domain.Enums;

/// <summary>
/// Nature d'une fermeture du calendrier scolaire (<c>SchoolClosure</c>). Stockée en string
/// (convention du projet). Purement informative : le cahier de roulement traite les trois de la
/// même façon (jour non travaillé), seul le libellé imprimé diffère.
/// </summary>
public enum SchoolClosureKind
{
    /// <summary>Jour férié ou fête légale/religieuse (Tabaski, Korité, 4 avril…).</summary>
    Holiday = 0,

    /// <summary>Vacances scolaires (Noël, Pâques, vacances d'été…).</summary>
    Vacation = 1,

    /// <summary>Autre fermeture : grève, intempéries, journée pédagogique…</summary>
    Other = 2
}
