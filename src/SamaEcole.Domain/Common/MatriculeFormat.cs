using System.Text.RegularExpressions;

namespace SamaEcole.Domain.Common;

/// <summary>
/// Rend un matricule à partir du gabarit paramétré par l'établissement (tickets JGK-B02 et JGK-D01,
/// openapi.yaml §SchoolSettings : « ELEV-{YEAR}-{SEQ:4} »).
///
/// Deux jetons, et deux seulement :
///   {YEAR}   -> millésime de l'année SCOLAIRE (bascule en octobre — voir AcademicYear).
///   {SEQ:n}  -> compteur, complété à n chiffres. {SEQ} sans n = pas de complétion.
///
/// Le gabarit est VALIDÉ avant d'être stocké : un format sans {SEQ} produirait le même matricule pour
/// tous les élèves de l'école, et la violation d'unicité n'apparaîtrait qu'à la deuxième inscription
/// — en pleine journée de rentrée. Mieux vaut refuser le réglage que subir la panne.
/// </summary>
public static partial class MatriculeFormat
{
    [GeneratedRegex(@"\{SEQ(?::(?<width>\d+))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex SequenceToken();

    [GeneratedRegex(@"\{(?!YEAR\}|SEQ\}|SEQ:\d+\})[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex UnknownToken();

    /// <summary>Longueur maximale du matricule rendu : cadrée par la colonne en base.</summary>
    public const int MaxRenderedLength = 50;

    public static string Render(string format, int year, int sequence)
    {
        var withYear = format.Replace("{YEAR}", year.ToString(), StringComparison.OrdinalIgnoreCase);

        return SequenceToken().Replace(withYear, match =>
        {
            var width = match.Groups["width"].Success
                ? int.Parse(match.Groups["width"].Value)
                : 0;

            return sequence.ToString($"D{width}");
        });
    }

    /// <summary>
    /// Valide un gabarit avant enregistrement. Renvoie null si tout va bien, sinon le motif du refus.
    /// </summary>
    public static string? Validate(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return "Le format est obligatoire.";
        }

        if (!SequenceToken().IsMatch(format))
        {
            return "Le format doit contenir {SEQ} ou {SEQ:n} — sans compteur, tous les matricules seraient identiques.";
        }

        var unknown = UnknownToken().Match(format);
        if (unknown.Success)
        {
            return $"Jeton inconnu « {unknown.Value} ». Jetons admis : {{YEAR}}, {{SEQ}}, {{SEQ:n}}.";
        }

        // Le compteur peut dépasser la largeur demandée (le 10 000e élève d'un {SEQ:4}) : on vérifie
        // donc la longueur sur un numéro volontairement large, pas sur le premier de la série.
        var rendered = Render(format, 2026, 999_999);
        if (rendered.Length > MaxRenderedLength)
        {
            return $"Le matricule produit dépasse {MaxRenderedLength} caractères.";
        }

        return null;
    }
}
