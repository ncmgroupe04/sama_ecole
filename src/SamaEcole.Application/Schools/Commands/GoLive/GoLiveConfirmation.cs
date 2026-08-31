namespace SamaEcole.Application.Schools.Commands.GoLive;

/// <summary>
/// Garde du passage en mode réel, isolée ici — comme <see cref="ResetSchoolData.ResetSchoolDataConfirmation"/>
/// — pour que le validateur, le Handler et l'écran citent la MÊME règle. Le pendant JavaScript vit
/// dans settings.js (goLiveConfirmationMatches).
/// </summary>
public static class GoLiveConfirmation
{
    /// <summary>Mot-clé attendu, à la casse près.</summary>
    public const string Keyword = "CONFIRMER";

    /// <summary>
    /// Le mot-clé est comparé À LA CASSE (« confirmer » ne déclenche rien) : c'est le geste délibéré
    /// qui fait la valeur de la garde. Le nom de l'établissement, lui, est comparé sans tenir compte
    /// de la casse ni des espaces de bord — le Directeur le recopie, il n'a pas à en reproduire la
    /// graphie exacte. Un nom d'école vide ne valide jamais.
    /// </summary>
    public static bool Matches(string? typedConfirmation, string? schoolName)
    {
        var typed = typedConfirmation?.Trim();

        if (string.IsNullOrEmpty(typed))
        {
            return false;
        }

        if (string.Equals(typed, Keyword, StringComparison.Ordinal))
        {
            return true;
        }

        var name = schoolName?.Trim();

        return !string.IsNullOrEmpty(name)
            && string.Equals(typed, name, StringComparison.OrdinalIgnoreCase);
    }
}
