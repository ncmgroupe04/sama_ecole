namespace SamaEcole.Application.OptionalSubjects;

/// <summary>
/// Règles PURES (aucun accès base) des matières optionnelles : normalisation d'un groupe, appariement de
/// niveau, validation d'un choix et dispenses qui en découlent (spécification §4.3, §5.2).
/// </summary>
public static class OptionSelectionRules
{
    /// <summary>Groupe nettoyé : blanc → null, espaces de bord retirés.</summary>
    public static string? NormalizeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();
}
