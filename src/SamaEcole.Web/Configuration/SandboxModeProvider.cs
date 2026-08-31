using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Web.Configuration;

/// <summary>
/// Implémentation de <see cref="ISandboxModeProvider"/> : valeur figée au démarrage (singleton),
/// calculée dans Program.cs.
///
/// <c>RevertToTestEnabled</c> vaut <c>true</c> en environnement Development, OU si la variable
/// d'environnement <c>SAMA_RETOUR_MODE_TEST_AUTORISE</c> vaut <c>true</c>. Partout ailleurs — donc en
/// vraie production, où la variable n'est jamais posée — il vaut <c>false</c>, et le passage en mode
/// réel est définitif.
///
/// Le choix de NE PAS se fier à <c>IHostEnvironment.IsProduction()</c> est délibéré : les
/// environnements de recette tournent souvent avec l'image de production
/// (<c>ASPNETCORE_ENVIRONMENT=Production</c>). Une variable dédiée, posée uniquement sur les
/// environnements jetables, est le seul discriminant fiable.
/// </summary>
public sealed class SandboxModeProvider(bool revertToTestEnabled) : ISandboxModeProvider
{
    public bool RevertToTestEnabled { get; } = revertToTestEnabled;
}
