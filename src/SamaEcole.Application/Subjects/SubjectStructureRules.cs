namespace SamaEcole.Application.Subjects;

/// <summary>
/// Bornes de FORME des champs de structure d'évaluation, partagées mot pour mot par
/// CreateSubjectCommandValidator et UpdateSubjectCommandValidator : une règle acceptée à la création et
/// refusée à la modification (ou l'inverse) laisserait l'école bloquée sur une grille qu'elle vient
/// pourtant d'enregistrer.
///
/// Rien ici ne touche la base : les invariants de hiérarchie (le domaine existe-t-il ? la profondeur
/// est-elle tenue ?) vivent dans <see cref="SubjectHierarchyGuard"/>, côté Handler.
/// </summary>
internal static class SubjectStructureRules
{
    /// <summary>
    /// Plafond d'un barème de ligne. 100 couvre largement les grilles observées (la plus haute est /60)
    /// tout en restant sous la précision numeric(5,2) de la colonne — au-delà, l'erreur serait rendue par
    /// PostgreSQL en 500 plutôt que sur le champ, en 422.
    /// </summary>
    public const decimal MaxScoreCeiling = 100m;

    public const string MaxScoreMessage =
        "La note maximale (« Sur ») doit être supérieure à zéro et ne pas dépasser 100.";

    /// <summary>Null est LÉGAL : la ligne suit alors le barème du cycle de la classe (Subject.MaxScore).</summary>
    public static bool IsValidMaxScore(decimal? maxScore) =>
        maxScore is not { } value || (value > 0m && value <= MaxScoreCeiling);
}
