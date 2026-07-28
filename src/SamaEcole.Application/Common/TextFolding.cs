using System.Globalization;
using System.Text;

namespace SamaEcole.Application.Common;

/// <summary>
/// Forme de COMPARAISON d'un texte saisi : majuscules, accents retirés. Sert à reconnaître une valeur
/// écrite par un utilisateur (« Lycée », « LYCEE » et « lycee » se valent) — jamais à la stocker ni à
/// l'afficher : la valeur d'origine reste toujours celle qui est persistée et imprimée.
///
/// Partagé par <c>ClassroomCycle</c> (reconnaître un niveau) et <c>SchoolHeading</c> (reconnaître un
/// préfixe de cycle) : le même pliage Unicode aux deux endroits, jamais deux copies qui divergent.
/// </summary>
public static class TextFolding
{
    public static string Fold(string value)
    {
        var decomposed = value.ToUpperInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
