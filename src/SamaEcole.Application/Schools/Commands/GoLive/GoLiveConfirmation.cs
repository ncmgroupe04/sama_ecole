using SamaEcole.Application.Common;

namespace SamaEcole.Application.Schools.Commands.GoLive;

/// <summary>
/// Garde du passage en mode réel, isolée ici — comme <see cref="ResetSchoolData.ResetSchoolDataConfirmation"/>
/// et <see cref="RevertToTest.RevertToTestConfirmation"/> — pour que le validateur, le Handler et
/// l'écran citent la MÊME règle. L'algorithme lui-même vit dans <see cref="TypedConfirmationGuard"/>,
/// partagé par les trois : seul le mot-clé change ici. Le pendant JavaScript vit dans settings.js
/// (goLiveConfirmationMatches).
/// </summary>
public static class GoLiveConfirmation
{
    /// <summary>Mot-clé attendu, à la casse près.</summary>
    public const string Keyword = "CONFIRMER";

    public static bool Matches(string? typedConfirmation, string? schoolName)
        => TypedConfirmationGuard.Matches(typedConfirmation, Keyword, schoolName);
}
