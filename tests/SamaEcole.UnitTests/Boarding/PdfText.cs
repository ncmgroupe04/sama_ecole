using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Texte de toutes les pages d'un PDF, espaces normalisés — pour contrôler ce qu'un document imprimé EXPOSE vraiment
/// (confidentialité de la fiche médicale, absence de téléphone dans le registre). Aide de test uniquement : PdfPig n'est
/// référencé que par les projets de tests.
/// </summary>
internal static partial class PdfText
{
    public static string Extract(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var text = string.Join(" ", document.GetPages().Select(page => page.Text));
        return Whitespace().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
