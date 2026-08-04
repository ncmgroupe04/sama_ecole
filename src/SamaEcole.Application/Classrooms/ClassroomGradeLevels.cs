using System.Text.RegularExpressions;
using SamaEcole.Application.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Classrooms;

/// <summary>
/// Nomenclature des NIVEAUX (CI, CP, CE1… Sixième… Terminale) d'un cycle, dans l'ordre pédagogique.
///
/// À ne pas confondre avec <see cref="Domain.Entities.Classroom.Level"/>, qui porte le CYCLE en texte
/// libre (« Primaire », « Collège »…) et pilote le barème via <see cref="ClassroomCycle"/>. Le niveau
/// réel d'une classe, lui, n'a jamais eu de colonne : il vit dans son <see cref="Domain.Entities.Classroom.Name"/>
/// (« CM2 B », « 6e A »), et c'est délibéré — AGENTS.md interdit d'introduire une énumération de niveaux,
/// chaque établissement sénégalais nommant ses classes comme il l'entend.
///
/// Cette table sert donc à DEUX choses, et à rien d'autre :
///   1. peupler la liste déroulante « second niveau validé » du formulaire de classe accélérée
///      (servie à la vue Razor, pas recopiée en JavaScript — une seule source) ;
///   2. reconnaître le niveau courant d'une classe depuis son nom, pour que la délibération d'une
///      classe passerelle puisse valider le niveau courant ET le niveau cible (voir <see cref="ClassroomPromotion"/>).
///
/// La reconnaissance est TOLÉRANTE (casse, accents, espace intercalaire, séparateur de passerelle) parce
/// que le nom reste saisi librement : « 6e A », « 6 ème A », « Sixième A » et « 6E-5E » désignent tous
/// une Sixième. Un nom hors nomenclature n'est pas une erreur — il retourne simplement null, et
/// l'appelant retombe sur le nom tel qu'il a été saisi.
/// </summary>
public static class ClassroomGradeLevels
{
    /// <summary>
    /// Libellé de chaque niveau + la forme sous laquelle un nom de classe peut le porter. L'ORDRE est
    /// l'ordre pédagogique du cycle : c'est lui qui décide de la liste déroulante, et il fait aussi que
    /// « TPS » est testé avant « PS » (sans quoi une Toute Petite Section serait prise pour une Petite
    /// Section). Motifs écrits SANS accents : ils sont testés sur le nom plié par <see cref="TextFolding"/>.
    /// </summary>
    private static readonly Dictionary<CycleType, (string Label, Regex Pattern)[]> Grades = new()
    {
        [CycleType.Maternelle] =
        [
            ("TPS", Starts(@"TPS|TOUTE\s*PETITE\s*SECTION")),
            ("PS", Starts(@"PS|PETITE\s*SECTION")),
            ("MS", Starts(@"MS|MOYENNE\s*SECTION")),
            ("GS", Starts(@"GS|GRANDE\s*SECTION"))
        ],
        [CycleType.Primaire] =
        [
            ("CI", Starts(@"CI")),
            ("CP", Starts(@"CP")),
            ("CE1", Starts(@"CE\s*1")),
            ("CE2", Starts(@"CE\s*2")),
            ("CM1", Starts(@"CM\s*1")),
            ("CM2", Starts(@"CM\s*2"))
        ],
        [CycleType.College] =
        [
            ("Sixième", Starts(@"6\s*(?:EME|E)?|SIXIEME")),
            ("Cinquième", Starts(@"5\s*(?:EME|E)?|CINQUIEME")),
            ("Quatrième", Starts(@"4\s*(?:EME|E)?|QUATRIEME")),
            ("Troisième", Starts(@"3\s*(?:EME|E)?|TROISIEME"))
        ],
        [CycleType.Lycee] =
        [
            ("Seconde", Starts(@"2\s*(?:NDE|ND|DE|E)?|SECONDE")),
            ("Première", Starts(@"1\s*(?:ERE|RE|ER|E)?|PREMIERE")),
            ("Terminale", Starts(@"T(?:LE|ERM)|TERMINALE"))
        ]
    };

    /// <summary>
    /// Le motif doit couvrir le DÉBUT du nom et s'arrêter sur une frontière : l'espace d'un suffixe de
    /// série (« CM2 B »), la fin du nom (« CM2 »), ou le séparateur d'une classe passerelle (« CI-CP »,
    /// « 6e/5e ») — sans quoi le niveau courant d'une classe accélérée, justement, ne serait pas reconnu.
    /// </summary>
    private static Regex Starts(string body) =>
        new($@"^(?:{body})(?:[\s\-/]|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Niveaux du cycle, dans l'ordre pédagogique. Cycle inconnu → liste vide, jamais null.</summary>
    public static IReadOnlyList<string> For(CycleType cycle) =>
        Grades.TryGetValue(cycle, out var grades) ? Array.ConvertAll(grades, g => g.Label) : [];

    /// <summary>Même chose depuis le niveau textuel d'une classe (« Primaire », « Lycée »…).</summary>
    public static IReadOnlyList<string> For(string? level) => For(ClassroomCycle.CycleFor(level));

    /// <summary>
    /// Niveau porté par le NOM de la classe : « CM2 B » → « CM2 », « 6 ème A » → « Sixième »,
    /// « CI-CP » → « CI ». Null si le nom ne suit aucune nomenclature connue (classe hors norme, donnée
    /// héritée) — l'appelant décide alors quoi afficher, plutôt que de recevoir un niveau inventé.
    /// </summary>
    public static string? FromClassroomName(string? classroomName, CycleType cycle)
    {
        if (string.IsNullOrWhiteSpace(classroomName) || !Grades.TryGetValue(cycle, out var grades))
        {
            return null;
        }

        var folded = TextFolding.Fold(classroomName.Trim());

        foreach (var (label, pattern) in grades)
        {
            if (pattern.IsMatch(folded))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>
    /// Vrai si <paramref name="level"/> figure dans la nomenclature du cycle, à la casse et aux accents
    /// près. Sert à la validation du second niveau d'une classe accélérée : le champ vient d'une liste
    /// déroulante, mais l'API reste ouverte et un appel direct ne doit pas y glisser n'importe quoi.
    /// </summary>
    public static bool IsKnown(string? level, CycleType cycle)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return false;
        }

        var folded = TextFolding.Fold(level.Trim());
        return For(cycle).Any(known => TextFolding.Fold(known) == folded);
    }
}
