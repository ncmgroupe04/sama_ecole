namespace SamaEcole.Domain.Enums;

/// <summary>
/// Regroupement pédagogique d'une matière (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.1). Purement descriptif : ne pilote aucune règle de
/// calcul de moyenne, aucun contrôle d'accès — <see cref="Entities.Subject.Coefficient"/> et le
/// calcul du bulletin restent inchangés. <see cref="French"/> en premier membre et valeur par
/// défaut : toute matière existante avant cette migration reste tacitement "française", sans
/// changement de comportement.
/// </summary>
public enum SectionType
{
    French,
    Arabic,
    IslamicStudies
}

/// <summary>
/// Classification d'établissement (module Coran/Franco-Arabe, spec §3.2). PUREMENT INFORMATIF :
/// n'active rien seul, ne remplace pas <see cref="Entities.SchoolSettings.IsCoranModuleEnabled"/>
/// (le seul interrupteur consommé par <c>[RequireModule(SchoolModule.Coran)]</c>) ni
/// <see cref="TypeEtablissement"/> (axe Privé/Public, sans rapport). <see cref="Standard"/> en
/// premier membre et valeur par défaut : toute école existante reste "standard" sans migration
/// manuelle, comme <see cref="TypeEtablissement.Prive"/>.
/// </summary>
public enum SchoolType
{
    Standard,
    FrancoArabic,
    Daara
}

/// <summary>
/// Statut de mémorisation d'une portion du Coran (<see cref="Entities.QuranProgress"/>, spec §3.3).
/// <see cref="InProcess"/> en premier membre et valeur par défaut : une ligne de suivi commence
/// toujours en cours de mémorisation.
/// </summary>
public enum QuranMemorizationStatus
{
    InProcess,
    Memorized,
    Revised
}
