using SamaEcole.Application.Common;

namespace SamaEcole.Application.Schools.Commands.LockProduction;

/// <summary>
/// Garde du verrouillage définitif, isolée ici — comme <see cref="GoLive.GoLiveConfirmation"/>,
/// <see cref="ResetSchoolData.ResetSchoolDataConfirmation"/> et
/// <see cref="RevertToTest.RevertToTestConfirmation"/> — pour que le validateur, le Handler et
/// l'écran citent la MÊME règle. L'algorithme lui-même vit dans <see cref="TypedConfirmationGuard"/>,
/// partagé par les quatre : seul le mot-clé change ici. Le pendant JavaScript vit dans settings.js
/// (lockProductionConfirmationMatches).
/// </summary>
public static class LockProductionConfirmation
{
    /// <summary>Mot-clé attendu, à la casse près.</summary>
    public const string Keyword = "VERROUILLER";

    public static bool Matches(string? typedConfirmation, string? schoolName)
        => TypedConfirmationGuard.Matches(typedConfirmation, Keyword, schoolName);
}
