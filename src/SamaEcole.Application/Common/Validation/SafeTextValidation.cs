using System.Text.RegularExpressions;
using FluentValidation;

namespace SamaEcole.Application.Common.Validation;

/// <summary>
/// Ticket JGK-F01 (stabilisation) — défense en profondeur contre l'injection HTML/script dans les
/// champs textuels libres. La PREMIÈRE ligne de défense reste l'encodage à la sortie (Razor <c>@</c>,
/// Alpine <c>x-text</c>) ; cette règle refuse en amont ce qu'aucun nom, adresse ou libellé légitime
/// ne contient de toute façon.
///
/// Ce qui est bloqué — et pourquoi pas davantage :
/// <list type="bullet">
/// <item><c>&lt;</c> et <c>&gt;</c> : aucune balise HTML (dont <c>&lt;script&gt;</c>) ne peut se former sans eux.</item>
/// <item><c>javascript:</c> (espaces tolérés autour du <c>:</c>) : neutralise l'injection d'URI dans un attribut.</item>
/// <item><c>&amp;#</c> : entités numériques (<c>&amp;#60;</c> = <c>&lt;</c>) qui referaient surface au premier décodage.</item>
/// </list>
/// Le MOT « script » n'est volontairement PAS bloqué : « inscription », « description » ou
/// « prescription » le contiennent — le vocabulaire même de cette application. Sans <c>&lt;</c> ni
/// <c>&gt;</c>, la sous-chaîne est inoffensive.
/// </summary>
public static partial class SafeTextValidation
{
    public const string ErrorMessage =
        "Le texte contient des caractères interdits (« < », « > » ou une séquence de script).";

    [GeneratedRegex(@"[<>]|javascript\s*:|&#", RegexOptions.IgnoreCase)]
    private static partial Regex SuspiciousContent();

    /// <summary>Vrai si la chaîne est saine (ou absente — NotEmpty reste l'affaire de chaque validateur).</summary>
    public static bool IsSafeText(string? value) =>
        value is null || !SuspiciousContent().IsMatch(value);

    /// <summary>
    /// Refuse les caractères permettant une injection HTML/script. À poser sur tout champ textuel
    /// LIBRE stocké puis réaffiché (nom, adresse, libellé, motif, recherche…) — jamais sur un mot de
    /// passe (haché, jamais réaffiché : le restreindre n'apporte rien et affaiblit les phrases de passe).
    /// </summary>
    public static IRuleBuilderOptions<T, string?> NoHtml<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder.Must(IsSafeText).WithMessage(ErrorMessage);
}
