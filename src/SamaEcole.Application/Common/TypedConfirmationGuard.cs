namespace SamaEcole.Application.Common;

/// <summary>
/// Algorithme de confirmation partagé par les gardes d'action critique de l'établissement
/// (<c>GoLiveConfirmation</c>, <c>ResetSchoolDataConfirmation</c>, <c>RevertToTestConfirmation</c>) :
/// un mot-clé imposé À LA CASSE, ou le nom exact de l'établissement toléré en casse et en espaces de
/// bord. Centralisé ici pour qu'un changement de règle (un mode insensible à la casse, un mot-clé qui
/// tolère la ponctuation…) se fasse UNE fois plutôt que trois, sans quoi les trois variantes
/// dériveraient silencieusement l'une de l'autre.
/// </summary>
public static class TypedConfirmationGuard
{
    /// <summary>
    /// Le mot-clé est comparé À LA CASSE : c'est le geste délibéré qui fait la valeur de la garde. Le
    /// nom de l'établissement, lui, est comparé sans tenir compte de la casse ni des espaces de bord —
    /// le Directeur le recopie, il n'a pas à en reproduire la graphie exacte. Un nom d'école vide ne
    /// valide jamais.
    /// </summary>
    public static bool Matches(string? typedConfirmation, string keyword, string? schoolName)
    {
        var typed = typedConfirmation?.Trim();

        if (string.IsNullOrEmpty(typed))
        {
            return false;
        }

        if (string.Equals(typed, keyword, StringComparison.Ordinal))
        {
            return true;
        }

        var name = schoolName?.Trim();

        return !string.IsNullOrEmpty(name)
            && string.Equals(typed, name, StringComparison.OrdinalIgnoreCase);
    }
}
