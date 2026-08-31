namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Expose aux Handlers le drapeau d'environnement « le retour au mode test est-il autorisé ici ? ».
///
/// Calculé UNE FOIS au démarrage (voir SandboxModeProvider dans SamaEcole.Web) :
/// <c>true</c> en environnement Development, ou si <c>SAMA_RETOUR_MODE_TEST_AUTORISE=true</c> ;
/// <c>false</c> partout ailleurs — c'est-à-dire en vraie production, où le passage en mode réel est
/// DÉFINITIF.
///
/// Le drapeau n'est PAS conditionné au build (<c>ASPNETCORE_ENVIRONMENT</c>) : les environnements de
/// recette tournent souvent avec l'image de production (<c>Production</c>), et doivent malgré tout
/// pouvoir rejouer la bascule. D'où une variable dédiée, posée uniquement sur les environnements
/// jetables.
/// </summary>
public interface ISandboxModeProvider
{
    /// <summary>
    /// <c>true</c> : l'endpoint <c>/schools/current/dev/revert-to-test</c> est monté et
    /// <c>RevertToTestCommand</c> s'exécute. <c>false</c> : la route n'existe pas (404) et le Handler
    /// refuse — le mode réel ne se quitte jamais.
    /// </summary>
    bool RevertToTestEnabled { get; }
}
