using SamaEcole.Application.Common;

namespace SamaEcole.Application.Schools.Commands.ResetSchoolData;

/// <summary>
/// Garde de déverrouillage de la purge, isolée ici — et non enfouie dans le Handler — pour être
/// testable sans base de données, et pour que le validateur, le Handler et l'écran citent la MÊME
/// règle que <see cref="GoLive.GoLiveConfirmation"/> et <see cref="RevertToTest.RevertToTestConfirmation"/>.
/// L'algorithme lui-même vit dans <see cref="TypedConfirmationGuard"/>, partagé par les trois : seul
/// le mot-clé change ici (le pendant JavaScript vit dans settings.js : resetConfirmationMatches).
/// </summary>
public static class ResetSchoolDataConfirmation
{
    /// <summary>Mot-clé attendu, à la casse près.</summary>
    public const string Keyword = "PURGER";

    public static bool Matches(string? typedConfirmation, string? schoolName)
        => TypedConfirmationGuard.Matches(typedConfirmation, Keyword, schoolName);
}
