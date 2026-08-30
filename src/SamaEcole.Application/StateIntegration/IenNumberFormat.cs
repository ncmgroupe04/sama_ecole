namespace SamaEcole.Application.StateIntegration;

/// <summary>
/// La FORME d'un IEN provisoire de secours, et le contrôle de forme d'un IEN saisi — Volume 1 §23.1.
///
/// Vit dans Application, et non dans le générateur de Persistence : la forme est un concept de
/// CONTRAT (l'export Planète la lit, le validateur d'IEN la contrôle, les tests l'exercent), pas un
/// détail de stockage. <see cref="Persistence" langword="false"/> — le générateur ne fait qu'y
/// ajouter la séquence issue de la base.
///
/// ⚠ Rien ici n'authentifie un IEN. Un IEN OFFICIEL est délivré par le SIMEN, dont nous ne
/// connaissons pas le format ; nous ne fabriquons que des numéros PROVISOIRES, marqués comme tels.
/// Le chiffre de contrôle Luhn n'attrape que la faute de frappe et l'inversion de deux chiffres.
///
/// FORMAT PROVISOIRE — 15 caractères : <c>P</c> + code établissement (6, complété à gauche par des
/// zéros) + millésime scolaire sur 2 chiffres + séquence sur 5 chiffres + 1 chiffre de contrôle.
/// Exemple : <c>P01234726000175</c>. Le préfixe <c>P</c> est délibéré : il rend un numéro provisoire
/// reconnaissable À L'ŒIL, sur un papier, par un agent qui n'a accès à aucune base.
/// </summary>
public static class IenNumberFormat
{
    /// <summary>Marqueur de tête : un IEN provisoire doit se reconnaître sans consulter la base.</summary>
    public const char ProvisionalPrefix = 'P';

    private const int SchoolCodeLength = 6;
    private const int SequenceLength = 5;

    /// <summary>Longueur totale attendue d'un provisoire, préfixe et chiffre de contrôle compris.</summary>
    public const int ProvisionalIenLength = 1 + SchoolCodeLength + 2 + SequenceLength + 1;

    /// <summary>
    /// Assemble un IEN PROVISOIRE. Seuls les chiffres du code établissement sont retenus (les fichiers
    /// de l'administration le publient tantôt « 012347 », tantôt « IA-01/2347 ») ; tronqué à GAUCHE
    /// s'il dépasse — c'est la fin du code qui discrimine deux écoles voisines, jamais son préfixe
    /// régional.
    /// </summary>
    public static string ComposeProvisional(string schoolCode, int academicYear, int sequence)
    {
        var digits = new string((schoolCode ?? "").Where(char.IsDigit).ToArray());
        var normalizedCode = digits.Length >= SchoolCodeLength
            ? digits[^SchoolCodeLength..]
            : digits.PadLeft(SchoolCodeLength, '0');

        var body = $"{normalizedCode}{Mod(academicYear, 100):D2}{Mod(sequence, 100000):D5}";

        return $"{ProvisionalPrefix}{body}{ComputeCheckDigit(body)}";
    }

    /// <summary>
    /// Chiffre de contrôle Luhn du corps numérique. Détecte toute erreur d'UN chiffre et la quasi-
    /// totalité des transpositions de deux chiffres adjacents — les deux fautes de ressaisie réelles.
    /// </summary>
    public static int ComputeCheckDigit(string body)
    {
        var sum = 0;
        var doubling = true;

        // De droite à gauche : le chiffre de contrôle occupera la position la plus à droite.
        for (var i = body.Length - 1; i >= 0; i--)
        {
            var digit = body[i] - '0';

            if (doubling)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubling = !doubling;
        }

        return (10 - sum % 10) % 10;
    }

    /// <summary>
    /// Contrôle de FORME d'un IEN saisi. Ne dit RIEN de son existence réelle au fichier national.
    ///
    /// Un IEN officiel n'a aucune raison de suivre notre format provisoire — nous ne connaissons pas
    /// le sien. On accepte donc, pour un numéro qui ne porte pas notre préfixe, toute chaîne
    /// alphanumérique de longueur plausible (le garde-fou contre la faute de frappe), et on ne valide
    /// réellement la clé Luhn que sur NOS numéros, reconnaissables à leur <c>P</c> initial.
    /// </summary>
    public static bool IsWellFormed(string? ienNumber)
    {
        if (string.IsNullOrWhiteSpace(ienNumber))
        {
            return false;
        }

        var candidate = ienNumber.Trim().ToUpperInvariant();

        if (candidate.Length is < 8 or > 24 || !candidate.All(char.IsLetterOrDigit))
        {
            return false;
        }

        if (candidate[0] != ProvisionalPrefix)
        {
            // Numéro officiel présumé : forme plausible, authenticité invérifiable ici.
            return true;
        }

        return candidate.Length == ProvisionalIenLength
               && candidate[1..].All(char.IsDigit)
               && ComputeCheckDigit(candidate[1..^1]) == candidate[^1] - '0';
    }

    /// <summary>Vrai si le numéro porte le marqueur des provisoires (après normalisation).</summary>
    public static bool IsProvisional(string? ienNumber) =>
        !string.IsNullOrWhiteSpace(ienNumber)
        && char.ToUpperInvariant(ienNumber.Trim()[0]) == ProvisionalPrefix;

    // % en C# peut renvoyer un négatif pour un opérande négatif : une séquence ou un millésime ne
    // sont jamais négatifs en pratique, mais on s'en prémunit plutôt que d'émettre « P0123472-0007 ».
    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
