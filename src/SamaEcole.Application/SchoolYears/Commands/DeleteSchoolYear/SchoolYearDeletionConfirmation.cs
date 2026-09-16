namespace SamaEcole.Application.SchoolYears.Commands.DeleteSchoolYear;

/// <summary>
/// Garde de déverrouillage de la suppression d'une année scolaire, isolée ici — comme
/// <c>ResetSchoolDataConfirmation</c> — pour être testable sans base de données et pour que le
/// Handler et l'écran citent la MÊME règle (le pendant JavaScript vit dans school-years.js :
/// deleteConfirmationMatches).
///
/// Ici, PAS de mot-clé fixe : c'est le LIBELLÉ EXACT de l'année visée qu'il faut recopier
/// (« 2025-2026 »). Un mot générique se taperait de mémoire, sur la mauvaise ligne du tableau ; le
/// libellé, lui, oblige à regarder l'année qu'on s'apprête à supprimer.
/// </summary>
public static class SchoolYearDeletionConfirmation
{
    /// <summary>
    /// Comparaison sans tenir compte de la casse ni des espaces de bord : le libellé est recopié à la
    /// main, et « 2025-2026 » ne doit pas échouer pour une espace finale. Un libellé vide ne peut
    /// JAMAIS valider — sans cette réserve, une saisie vide suffirait sur une année mal nommée.
    /// </summary>
    public static bool Matches(string? typedConfirmation, string? yearLabel)
    {
        var typed = typedConfirmation?.Trim();
        var label = yearLabel?.Trim();

        return !string.IsNullOrEmpty(typed)
            && !string.IsNullOrEmpty(label)
            && string.Equals(typed, label, StringComparison.OrdinalIgnoreCase);
    }
}
