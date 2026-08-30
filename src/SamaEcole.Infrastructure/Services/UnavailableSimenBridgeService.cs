using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Services;

/// <summary>
/// Implémentation LIVRÉE du relais SIMEN : elle refuse chaque appel, explicitement (ticket JGK-M03).
///
/// Pourquoi livrer une implémentation qui ne fait rien plutôt que rien du tout : sans elle, le premier
/// câblage inventerait une URL, un schéma d'authentification et un contrat de réponse — trois
/// suppositions qui survivraient jusqu'en production. Un refus explicite est une information exacte ;
/// un succès simulé serait un mensonge que l'écran répercuterait à l'utilisateur (« 214 élèves
/// transmis » alors que rien n'est parti, et que l'école croirait sa déclaration faite).
///
/// Elle sera remplacée par <c>HttpSimenBridgeService</c> le jour où le ministère publiera son API —
/// même contrat, mêmes garde-fous (secret en configuration, webhook signé HMAC, appel journalisé).
/// D'ici là, <see cref="IsConfigured"/> vaut faux et l'interface n'affiche pas l'action.
/// </summary>
public class UnavailableSimenBridgeService(
    StateIntegrationSettings settings,
    ILogger<UnavailableSimenBridgeService> logger) : ISimenBridgeService
{
    private const string Reason =
        "Le relais SIMEN n'est pas disponible : aucune API publique n'est ouverte à ce jour et "
        + "aucun point d'accès n'est configuré. Utilisez l'export Planète (.csv/.json) et transmettez "
        + "le fichier par la voie habituelle.";

    /// <summary>
    /// Faux tant qu'aucune URL n'est configurée. Même si une URL l'était, cette implémentation
    /// resterait incapable de dialoguer : elle le signale plutôt que de tenter un appel qui échouerait
    /// avec un message technique incompréhensible pour un directeur d'école.
    /// </summary>
    public bool IsConfigured => false;

    public Task<SimenTransmissionResult> TransmitStudentsAsync(
        PlaneteExportDto export, CancellationToken cancellationToken)
    {
        // Journalisé en information, pas en erreur : ce n'est pas une panne. Le contexte (effectif,
        // année) sert à mesurer la demande réelle avant d'investir dans le relais.
        logger.LogInformation(
            "Transmission SIMEN demandée pour {StudentCount} élèves ({SchoolYear}) — relais non configuré ({ConfiguredUrl}).",
            export.StudentCount,
            export.SchoolYearLabel,
            settings.SimenApiBaseUrl ?? "aucune URL");

        return Task.FromResult(SimenTransmissionResult.NotConfigured(Reason));
    }

    /// <summary>
    /// Renvoie une LISTE VIDE, et non une liste de résultats à IEN nul : « je n'ai rien trouvé » et
    /// « je n'ai pas cherché » sont deux réponses différentes, et la seconde ne doit pas se déguiser
    /// en la première. Un appelant qui recevrait N résultats vides pourrait en conclure que ces élèves
    /// sont inconnus du fichier national — une conclusion fausse.
    /// </summary>
    public Task<IReadOnlyList<SimenIenLookupResult>> LookupOfficialIensAsync(
        IReadOnlyList<SimenIenLookupRequest> candidates, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Recherche d'IEN officiels demandée pour {Count} élèves — relais SIMEN non configuré.",
            candidates.Count);

        return Task.FromResult<IReadOnlyList<SimenIenLookupResult>>([]);
    }
}
