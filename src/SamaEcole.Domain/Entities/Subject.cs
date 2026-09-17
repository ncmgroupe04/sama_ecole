using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Matière enseignée, rattachée à un niveau (ticket JGK-C03).
///
/// Le NIVEAU est un texte libre, exactement comme <see cref="Classroom.Level"/> : « Primaire »,
/// « Collège », « Terminale S2 »… Une même matière porte un coefficient DIFFÉRENT selon le niveau —
/// « Mathématiques » pèse 4 au primaire et 6 en série scientifique. C'est pourquoi la matière est
/// (Niveau, Nom) et non le seul Nom : dupliquer « Maths » entre deux niveaux est non seulement permis,
/// c'est le cas normal.
///
/// Le COEFFICIENT pilote le calcul des moyennes et des bulletins (docs/Volume_1_Cahier_des_Charges.md
/// §8.3 : « total des coefficients, total des points, moyenne générale »). Il n'est donc jamais
/// décoratif : une valeur fausse ici fausse tous les bulletins du niveau.
/// </summary>
public class Subject : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Nom de la matière en arabe (module Coran/Franco-Arabe), saisi librement par l'école — aucune
    /// traduction automatique. Null tant que le Directeur/Secrétariat ne l'a pas renseigné : le
    /// bulletin bilingue imprime alors la ligne sans son second nom, jamais une valeur inventée (même
    /// convention que <see cref="Column1Header"/>/<see cref="Column2Header"/>).
    /// </summary>
    public string? NameAr { get; set; }

    /// <summary>Niveau ou cycle, en texte libre — aligné sur la nomenclature des classes de l'établissement.</summary>
    public required string Level { get; set; }

    /// <summary>Poids de la matière dans la moyenne. Strictement positif, décimal (ex. 1,5 ; 4 ; 6).</summary>
    public decimal Coefficient { get; set; }

    /// <summary>
    /// DOMAINE parent, pour les grilles d'évaluation par compétences du primaire (APC) : « Lang &amp; Com. »
    /// porte « P. Alphabétique », « Vocabulaire », « Fluidité »… ; « Français » porte « Ressources » et
    /// « Compétences ». Null pour une matière de premier niveau — c'est le cas de TOUTE matière du
    /// secondaire, et l'état dans lequel se trouve l'intégralité des matières existantes.
    ///
    /// La hiérarchie est volontairement limitée à DEUX niveaux (domaine → activité) : c'est ce que
    /// montrent les grilles officielles, et une profondeur libre rendrait impossible le calcul du
    /// RowSpan d'une colonne unique sur le bulletin. Les commandes Create/Update refusent donc qu'une
    /// matière déjà rattachée à un domaine devienne elle-même le parent d'une autre.
    ///
    /// Un domaine parent n'est JAMAIS noté : il ne porte que le libellé de la première colonne, et sa
    /// note est celle de ses activités. CreateGradeCommandHandler refuse une note sur un parent.
    /// </summary>
    public Guid? ParentSubjectId { get; set; }

    /// <summary>
    /// Barème PROPRE à cette ligne d'évaluation — la colonne « Sur » du bulletin : 10, 16, 20, 24, 40, 60…
    /// selon la grille de l'école.
    ///
    /// NULL — et c'est la valeur de toutes les matières existantes — signifie « le barème du cycle de la
    /// classe » (Primaire /10, Collège &amp; Lycée /20, GradingScaleGuard.ScaleForCycle), c'est-à-dire
    /// exactement le comportement d'avant cette option. Renseigner ce champ n'est utile qu'aux grilles
    /// dont les lignes n'ont pas toutes le même maximum ; le laisser vide n'est pas une donnée manquante,
    /// c'est le choix de suivre le barème du cycle. Résolution unique : GradeCalculator.EffectiveMaxScore.
    /// </summary>
    public decimal? MaxScore { get; set; }

    /// <summary>
    /// Rang d'affichage dans sa fratrie (au sein d'un domaine pour une activité, au sein du niveau pour un
    /// domaine). L'ordre pédagogique d'une grille n'est ni alphabétique ni chronologique : « Ressources »
    /// précède toujours « Compétences », et rien dans les deux libellés ne le dit. À égalité, le nom
    /// départage — un ordre stable même si l'école n'a jamais touché aux flèches de réorganisation.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Entête de la 1re colonne du tableau du bulletin (« Domaines » au CI-CP, « Activités » au CE1-CE2).
    /// Porté par les DOMAINES parents uniquement, et lu sur le premier d'entre eux : c'est un réglage de
    /// grille, pas une propriété de ligne. Null → l'entête par défaut du bulletin.
    /// </summary>
    public string? Column1Header { get; set; }

    /// <summary>
    /// Entête de la 2e colonne (« Activités » au CI-CP, « Contrôles » au CE1-CE2). Même portée que
    /// <see cref="Column1Header"/>.
    /// </summary>
    public string? Column2Header { get; set; }
}
