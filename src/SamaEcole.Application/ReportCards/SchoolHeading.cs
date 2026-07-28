using SamaEcole.Application.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Troisième ligne de l'en-tête administratif du bulletin (« LYCÉE DE : Popenguine ») — le libellé de
/// cycle, et le nettoyage du nom saisi par l'école.
///
/// Le préfixe dépend du CYCLE DE LA CLASSE DE L'ÉLÈVE, jamais d'un réglage global d'établissement :
/// un même complexe scolaire édite des bulletins de CM2, de 5e et de Terminale, et « LYCÉE DE » codé en
/// dur s'imprimait jusqu'ici sur les trois. Même principe que le barème de notation
/// (<see cref="Grades.GradingScaleGuard"/>), résolu lui aussi par cycle.
///
/// Résolution faite ICI, dans Application, et non dans ReportCardDocument : le générateur PDF ne fait
/// que mettre en page, aucune règle métier ne descend dans l'Infrastructure (AGENTS.md règle #8, et
/// contrat explicite de ReportCardDataService).
///
/// La référence visuelle n'est pas contrainte sur ce point : docs/design-references/README.md décrit
/// cette ligne comme « nom de l'établissement », pas comme le littéral « LYCEE DE » — rendre le préfixe
/// dynamique reste fidèle à la référence (AGENTS.md règle #12).
/// </summary>
public static class SchoolHeading
{
    /// <summary>
    /// Libellé imprimé devant le nom, complément « DE » inclus (« COLLÈGE DE »). Termes GÉNÉRIQUES,
    /// valables dans le public comme dans le privé : « CEM » (Collège d'Enseignement Moyen) est propre à
    /// l'enseignement public, or <see cref="Domain.Entities.School"/> ne porte aucun indicateur
    /// public/privé permettant de trancher — on ne devine pas un statut administratif absent du modèle.
    /// </summary>
    public static string PrefixFor(CycleType cycle) => cycle switch
    {
        CycleType.Maternelle => "ÉCOLE MATERNELLE DE",
        CycleType.Primaire => "ÉCOLE ÉLÉMENTAIRE DE",
        CycleType.College => "COLLÈGE DE",
        _ => "LYCÉE DE"
    };

    /// <summary>Mots de cycle reconnus en tête d'une saisie, pliés (majuscules sans accents), les plus longs d'abord.</summary>
    private static readonly string[][] CycleWords =
    [
        ["ECOLE", "ELEMENTAIRE"],
        ["ECOLE", "PRIMAIRE"],
        ["ECOLE", "MATERNELLE"],
        ["COLLEGE"],
        ["LYCEE"],
        ["MATERNELLE"],
        ["CRECHE"],
        ["CEM"],
        ["ECOLE"]
    ];

    /// <summary>Liaison éventuelle entre le mot de cycle et le nom : « LYCÉE <b>DE</b> Popenguine ».</summary>
    private static readonly string[] Connectors = ["DE", "DU", "DES"];

    /// <summary>
    /// Retire le préfixe de cycle qu'une école a pu saisir dans <see cref="Domain.Entities.School.NomLycee"/> :
    /// le champ est libre, et son aide disait jusqu'ici « imprimé tel quel après LYCEE DE : » — beaucoup
    /// y ont donc écrit le nom complet. Sans ce nettoyage, un élève de 6e verrait
    /// « COLLÈGE DE : LYCÉE DE POPENGUINE ».
    ///
    /// Comparaison insensible à la casse ET aux accents (« Lycée », « LYCEE » et « lycee » se valent).
    /// Le nom d'origine est retourné INCHANGÉ si rien ne subsiste après le préfixe (une saisie réduite au
    /// seul mot « Lycée ») : mieux vaut un en-tête maladroit, visible et corrigeable par le Directeur,
    /// que la disparition silencieuse de la seule donnée qu'il ait renseignée.
    /// </summary>
    public static string? StripCyclePrefix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var original = name.Trim();
        var tokens = original.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var consumed = CycleWords
            .FirstOrDefault(words => words.Length <= tokens.Length
                                     && Enumerable.Range(0, words.Length).All(i => TextFolding.Fold(tokens[i]) == words[i]))
            ?.Length ?? 0;

        if (consumed == 0)
        {
            return original;
        }

        // Liaison « DE »/« DU »/« DES » en mot séparé, ou « D' » collée au nom (« LYCÉE D'ABC » → « ABC »).
        if (consumed < tokens.Length)
        {
            if (Connectors.Contains(TextFolding.Fold(tokens[consumed])))
            {
                consumed++;
            }
            else if (TextFolding.Fold(tokens[consumed]) is ['D', '\'' or '’', _, ..])
            {
                tokens[consumed] = tokens[consumed][2..];
            }
        }

        var remaining = string.Join(' ', tokens[consumed..]);

        return string.IsNullOrWhiteSpace(remaining) ? original : remaining;
    }
}
