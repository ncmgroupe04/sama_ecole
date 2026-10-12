namespace SamaEcole.Domain.Enums;

/// <summary>
/// Regroupement pédagogique d'une matière (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.1). Purement descriptif : ne pilote aucune règle de
/// calcul de moyenne, aucun contrôle d'accès — <see cref="Entities.Subject.Coefficient"/> et le
/// calcul du bulletin restent inchangés. <see cref="French"/> en premier membre et valeur par
/// défaut : toute matière existante avant cette migration reste tacitement "française", sans
/// changement de comportement.
///
/// <see cref="Common"/> (ajouté en dernier : les valeurs déjà stockées ne bougent pas) désigne le pôle
/// COMMUN du franco-arabe — la matière suivie par tous les élèves, qui n'appartient ni à la section
/// française ni à la section arabe (EPS, arts, vie scolaire…). Stocké en chaîne ≤ 20 caractères :
/// aucune migration n'est nécessaire.
/// </summary>
public enum SectionType
{
    French,
    Arabic,
    IslamicStudies,
    Common
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

/// <summary>
/// État d'avancement d'un élève sur UN Hizb (<see cref="Entities.StudentHizbStatus"/>, module
/// Internat/Daara). Dérivé de <see cref="Entities.StudentHizbStatus.CompletedQuarters"/> et verrouillé
/// avec lui par une contrainte CHECK : <see cref="NotStarted"/> ⇔ 0 quart, <see cref="InProgress"/> ⇔ 1 à 3,
/// <see cref="Completed"/> ⇔ 4. <see cref="NotStarted"/> en premier membre et valeur par défaut.
/// </summary>
public enum HizbMemorizationState
{
    NotStarted,
    InProgress,
    Completed
}
