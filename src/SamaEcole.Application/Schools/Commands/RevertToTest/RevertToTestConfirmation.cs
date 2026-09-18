using SamaEcole.Application.Common;

namespace SamaEcole.Application.Schools.Commands.RevertToTest;

/// <summary>
/// Garde du retour en mode test, isolée ici — comme <see cref="GoLive.GoLiveConfirmation"/> et
/// <see cref="ResetSchoolData.ResetSchoolDataConfirmation"/> — pour que le validateur, le Handler et
/// l'écran citent la MÊME règle. L'algorithme lui-même vit dans <see cref="TypedConfirmationGuard"/>,
/// partagé par les trois : seul le mot-clé change ici. Le pendant JavaScript vit dans settings.js
/// (revertToTestConfirmationMatches).
/// </summary>
public static class RevertToTestConfirmation
{
    /// <summary>Mot-clé attendu, à la casse près.</summary>
    public const string Keyword = "TEST";

    public static bool Matches(string? typedConfirmation, string? schoolName)
        => TypedConfirmationGuard.Matches(typedConfirmation, Keyword, schoolName);
}
