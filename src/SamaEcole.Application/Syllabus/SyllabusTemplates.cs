using SamaEcole.Application.Coefficients;

namespace SamaEcole.Application.Syllabus;

/// <summary>Une unité d'une trame nationale : partie, intitulé, volume horaire indicatif.</summary>
public sealed record SyllabusTemplateUnit(string? Section, string Title, decimal? PlannedHours = null);

/// <summary>Trame nationale d'une matière pour un niveau ; <see cref="Aliases"/> reconnaît les écritures courantes de la matière.</summary>
public sealed record SyllabusTemplate(string Subject, string[] Aliases, string GradeLevel, IReadOnlyList<SyllabusTemplateUnit> Units)
{
    public bool Matches(string subjectName)
    {
        var name = SeriesCoefficientTemplates.NormalizeName(subjectName);
        return name.Length > 0 && (SeriesCoefficientTemplates.NormalizeName(Subject) == name
                                   || Aliases.Any(a => SeriesCoefficientTemplates.NormalizeName(a) == name));
    }
}

/// <summary>
/// Trames du programme national (Évolution N°7, découpage DEMSGS / INEADE) — données en code, comme les modèles de
/// coefficients (arbitrage A10), importées par l'école dans SES unités (syllabus_units) où elle les ajuste.
///
/// Volontairement RESTREINTES : seules figurent ici les trames dont le découpage est stable et connu (Mathématiques de
/// 3e, programme du BFEM). Toute autre matière se saisit à l'écran « Programmes », en collant un chapitre par ligne —
/// jamais un programme inventé. Ajouter une trame = une entrée ici et un test.
/// </summary>
public static class SyllabusTemplates
{
    private static readonly string[] MathsAliases = ["Maths", "Math"];

    public static readonly IReadOnlyList<SyllabusTemplate> All =
    [
        new("Mathématiques", MathsAliases, "Troisième",
        [
            new("Activités numériques", "Racine carrée"),
            new("Activités numériques", "Applications affines"),
            new("Activités numériques", "Équations et inéquations du premier degré à une inconnue"),
            new("Activités numériques", "Systèmes d'équations et d'inéquations du premier degré à deux inconnues"),
            new("Activités numériques", "Statistiques"),
            new("Activités géométriques", "Théorème de Thalès"),
            new("Activités géométriques", "Relations trigonométriques dans le triangle rectangle"),
            new("Activités géométriques", "Angles inscrits"),
            new("Activités géométriques", "Vecteurs"),
            new("Activités géométriques", "Repérage dans le plan"),
            new("Activités géométriques", "Pyramide et cône")
        ])
    ];

    /// <summary>La trame d'une matière (par son nom) pour un niveau, ou null.</summary>
    public static SyllabusTemplate? For(string subjectName, string? gradeLevel) =>
        All.FirstOrDefault(t => t.GradeLevel == gradeLevel && t.Matches(subjectName));
}
