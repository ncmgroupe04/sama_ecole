using System.Text;

namespace SamaEcole.Application.Common;

/// <summary>
/// Mise en forme d'AFFICHAGE d'un numéro de téléphone sénégalais : « 770000000 » devient
/// « 77 000 00 00 ». Purement cosmétique — la valeur stockée n'est jamais modifiée : les numéros sont
/// saisis en texte libre (indicatif ou non, espaces ou non) et doivent le rester, sous peine de
/// réécrire des données que l'utilisateur a saisies volontairement.
///
/// Le Sénégal numérote sur 9 chiffres (mobiles 70/75/76/77/78, fixes 33), groupés 2‑3‑2‑2. L'indicatif
/// pays (+221, 00221, ou 221 accolé aux 9 chiffres) est reconnu et conservé sous sa forme « +221 ».
///
/// RÈGLE DE PRUDENCE : tout ce qui ne correspond pas exactement à ce gabarit est renvoyé TEL QUEL,
/// jamais regroupé de force. Un numéro étranger, un poste interne ou une saisie incomplète doit
/// s'afficher tel que l'école l'a enregistré — un regroupement 2‑3‑2‑2 appliqué à un numéro qui n'en
/// relève pas donnerait un faux numéro d'apparence crédible, bien pire qu'un affichage brut.
/// </summary>
public static class PhoneFormatter
{
    private const string CountryCode = "221";

    /// <summary>Longueur d'un numéro national sénégalais, indicatif pays exclu.</summary>
    private const int NationalLength = 9;

    /// <summary>Groupes du gabarit national 2‑3‑2‑2 (« 77 000 00 00 »).</summary>
    private static readonly int[] Groups = [2, 3, 2, 2];

    /// <summary>
    /// Numéro formaté pour l'affichage, ou la valeur d'origine si elle ne suit pas le gabarit
    /// sénégalais. <c>null</c> et les chaînes vides ressortent inchangés (l'appelant décide de son
    /// propre repli, « — » le plus souvent).
    /// </summary>
    public static string? FormatSenegal(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return phone;
        }

        var digits = KeepDigits(phone);
        if (digits.Length == 0)
        {
            return phone;
        }

        // Un « + » ailleurs qu'en tête n'est pas un indicatif : la saisie ne relève pas du gabarit.
        var trimmed = phone.Trim();
        var hasPlus = trimmed.StartsWith('+');
        if (trimmed.IndexOf('+', hasPlus ? 1 : 0) >= 0)
        {
            return phone;
        }

        var national = digits;
        var isInternational = false;

        // 00221XXXXXXXXX puis 221XXXXXXXXX : l'ordre compte, « 00221… » commence aussi par « 221 »
        // une fois les deux zéros retirés, jamais l'inverse.
        if (digits.StartsWith("00" + CountryCode, StringComparison.Ordinal))
        {
            national = digits[(2 + CountryCode.Length)..];
            isInternational = true;
        }
        else if (digits.StartsWith(CountryCode, StringComparison.Ordinal) && digits.Length == CountryCode.Length + NationalLength)
        {
            national = digits[CountryCode.Length..];
            isInternational = true;
        }

        if (national.Length != NationalLength)
        {
            return phone;
        }

        var builder = new StringBuilder(NationalLength + Groups.Length);

        // « + » conservé dès que l'indicatif était présent, quelle que soit la forme saisie (+221,
        // 00221, 221) : une seule écriture à l'affichage, plutôt que trois selon l'humeur de la saisie.
        if (isInternational || hasPlus)
        {
            builder.Append('+').Append(CountryCode).Append(' ');
        }

        var offset = 0;
        foreach (var size in Groups)
        {
            if (offset > 0)
            {
                builder.Append(' ');
            }

            builder.Append(national, offset, size);
            offset += size;
        }

        return builder.ToString();
    }

    /// <summary>Numéro formaté, ou <paramref name="fallback"/> si aucun numéro n'est renseigné.</summary>
    public static string FormatSenegalOr(string? phone, string fallback = "—") =>
        string.IsNullOrWhiteSpace(phone) ? fallback : FormatSenegal(phone)!;

    private static string KeepDigits(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else if (c is not (' ' or '.' or '-' or '/' or '(' or ')' or '+' or ' ' or '‑'))
            {
                // Lettre ou symbole inattendu (« poste 12 », « ext. ») : hors gabarit, on renonce.
                return string.Empty;
            }
        }

        return builder.ToString();
    }
}
