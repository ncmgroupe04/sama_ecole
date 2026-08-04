using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Classrooms;

/// <summary>
/// Moteur de délibération : QUELS niveaux un élève valide-t-il en fin d'année, une fois la décision du
/// conseil de classe prononcée (<see cref="ReportCardRemark.CouncilDecision"/>) ?
///
/// Règle ordinaire, inchangée : une classe = UN niveau. Un élève admis en CM2 valide le CM2.
///
/// Classe PASSERELLE / ACCÉLÉRÉE (<see cref="Classroom.IsAccelerated"/>, option désactivée par défaut) :
/// une seule année scolaire couvre DEUX niveaux successifs — « CI-CP » au primaire, « 6e-5e » pour les
/// filières d'intégration des daaras coraniques, où un élève arrivé tardivement rattrape son retard. Un
/// élève admis y valide donc le niveau courant ET le niveau cible (<see cref="Classroom.TargetLevel"/>),
/// sans quoi son historique académique le laisserait redémarrer au niveau qu'il vient précisément de sauter.
///
/// Fonction PURE, sans accès base : c'est la règle de délibération elle-même, testable telle quelle et
/// consommable aussi bien par le PV de délibération que par une future clôture d'année automatisée.
/// AGENTS.md règle #8 — elle ne vit ni dans un contrôleur ni dans l'entité.
/// </summary>
public static class ClassroomPromotion
{
    /// <summary>
    /// Formulation OFFICIELLE du dispositif, unique pour toute la plateforme : reçus, bulletin, PV de
    /// délibération et écrans en affichent tous exactement la même — un parent qui compare son reçu et
    /// le bulletin de son enfant doit y lire le même mot, pas deux variantes.
    /// </summary>
    public const string AcceleratedMention = "Cursus Accéléré Passerelle";

    /// <summary>
    /// Niveaux validés par un élève de <paramref name="classroom"/> à l'issue de l'année, dans l'ordre
    /// pédagogique (niveau courant d'abord).
    ///
    /// Seule la décision <see cref="CouncilDecision.Admitted"/> valide quoi que ce soit : un redoublement
    /// autorisé ou une exclusion ne valident RIEN, et une décision non encore prise (null) non plus —
    /// une liste vide dit « rien d'acquis à ce jour », jamais un niveau acquis par défaut.
    ///
    /// Un niveau cible identique au niveau courant (saisie contradictoire échappée à la validation, ou
    /// donnée héritée) ne compte qu'une fois : l'historique ne doit pas porter deux fois le même niveau.
    /// </summary>
    public static IReadOnlyList<string> ValidatedLevels(Classroom classroom, CouncilDecision? councilDecision)
    {
        ArgumentNullException.ThrowIfNull(classroom);

        if (councilDecision != CouncilDecision.Admitted)
        {
            return [];
        }

        var currentLevel = CurrentLevel(classroom);

        // Le drapeau ET le niveau cible, jamais l'un sans l'autre : une classe cochée « accélérée » mais
        // laissée sans second niveau reste délibérée comme une classe ordinaire — l'option est optionnelle
        // jusqu'au bout, elle ne peut pas dégrader une délibération existante.
        if (!classroom.IsAccelerated || string.IsNullOrWhiteSpace(classroom.TargetLevel))
        {
            return [currentLevel];
        }

        var targetLevel = classroom.TargetLevel.Trim();

        return string.Equals(targetLevel, currentLevel, StringComparison.OrdinalIgnoreCase)
            ? [currentLevel]
            : [currentLevel, targetLevel];
    }

    /// <summary>
    /// Valeur à PERSISTER dans <see cref="Classroom.TargetLevel"/>. Décocher « classe accélérée » efface
    /// le second niveau : sans cette normalisation, une classe redevenue ordinaire garderait un niveau
    /// cible orphelin, invisible à l'écran mais prêt à ressortir si la case était recochée.
    /// Partagée par la création et la correction — une seule règle, jamais deux copies qui divergent.
    /// </summary>
    public static string? NormalizeTargetLevel(bool isAccelerated, string? targetLevel) =>
        isAccelerated && !string.IsNullOrWhiteSpace(targetLevel) ? targetLevel.Trim() : null;

    /// <summary>
    /// Niveau courant de la classe : celui que porte son nom (« CM2 B » → « CM2 »). Un nom hors
    /// nomenclature retombe sur le nom lui-même, tel qu'il a été saisi — l'historique reste lisible
    /// plutôt que vide.
    /// </summary>
    public static string CurrentLevel(Classroom classroom)
    {
        ArgumentNullException.ThrowIfNull(classroom);

        return ClassroomGradeLevels.FromClassroomName(classroom.Name, classroom.Cycle)
               ?? classroom.Name.Trim();
    }

    /// <summary>
    /// Mention imprimée en en-tête des documents d'une classe accélérée (bulletin, PV de délibération) —
    /// « Cursus Accéléré Passerelle — CI → CP ». Null pour une classe ordinaire : rien ne s'imprime, le
    /// gabarit d'origine est strictement inchangé (AGENTS.md règle #12).
    /// </summary>
    public static string? AcceleratedPathLabel(Classroom classroom)
    {
        ArgumentNullException.ThrowIfNull(classroom);

        if (!classroom.IsAccelerated)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(classroom.TargetLevel)
            ? AcceleratedMention
            : $"{AcceleratedMention} — {CurrentLevel(classroom)} → {classroom.TargetLevel.Trim()}";
    }

    /// <summary>
    /// Libellé de la ligne « Classe » d'une pièce imprimée : « CI-CP (Cursus Accéléré Passerelle) » pour
    /// une classe passerelle, le nom seul sinon — le reçu d'une classe ordinaire ne change pas d'un
    /// caractère (AGENTS.md règle #12, la référence de design fait foi).
    ///
    /// Sur un reçu, le dispositif doit se voir SANS relire le règlement de l'école : c'est la seule
    /// pièce que le tuteur conserve, et elle atteste que l'année payée en couvre deux.
    /// </summary>
    public static string DisplayName(string classroomName, bool isAccelerated) =>
        isAccelerated ? $"{classroomName} ({AcceleratedMention})" : classroomName;
}
