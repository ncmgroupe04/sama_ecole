namespace SamaEcole.Application.Schools.Commands.ResetSchoolData;

/// <summary>
/// Garde de déverrouillage de la purge, isolée ici — et non enfouie dans le Handler — pour être
/// testable sans base de données, et pour que le validateur, le Handler et l'écran citent la MÊME
/// règle plutôt que trois variantes qui dérivent (le pendant JavaScript vit dans
/// settings.js : resetConfirmationMatches).
/// </summary>
public static class ResetSchoolDataConfirmation
{
    /// <summary>Mot-clé attendu, à la casse près.</summary>
    public const string Keyword = "PURGER";

    /// <summary>
    /// Le mot-clé est comparé À LA CASSE (« purger » ne déclenche rien) : c'est le geste délibéré qui
    /// fait la valeur de la garde. Le nom de l'établissement, lui, est comparé sans tenir compte de la
    /// casse ni des espaces de bord — le Directeur le recopie, il n'a pas à en reproduire la graphie
    /// exacte. Un nom d'école vide ne peut JAMAIS valider : sans cette réserve, une école mal
    /// renseignée se purgerait sur une saisie vide.
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
