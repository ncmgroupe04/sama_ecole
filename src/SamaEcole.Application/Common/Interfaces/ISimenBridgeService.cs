using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Relais vers le système d'information du ministère (SIMEN) — Volume 1 §23.2, ticket JGK-M03.
///
/// ⚠ AUCUNE API PUBLIQUE N'EXISTE À CE JOUR. Ce contrat est écrit maintenant pour que les Handlers
/// d'export soient structurés autour du bon découpage le jour où elle ouvrira ; l'implémentation
/// livrée est <c>UnavailableSimenBridgeService</c>, qui REFUSE explicitement chaque appel.
///
/// Pourquoi une implémentation qui refuse plutôt que rien du tout : sans elle, la première tentative
/// de câblage inventerait une URL, un format d'authentification et un contrat de réponse — trois
/// suppositions qui survivraient jusqu'en production. Un refus explicite est une information ; un
/// succès simulé est un mensonge que l'écran répercuterait à l'utilisateur (« 214 élèves transmis »
/// alors que rien n'est parti).
///
/// Contraintes à respecter le jour de l'implémentation réelle :
///   • Le secret d'API vit dans la configuration (jamais en base, jamais commité) — AGENTS.md.
///   • Un webhook entrant du SIMEN est signé (HMAC) et vérifié AVANT toute écriture, comme les
///     webhooks de paiement (AGENTS.md règle #11). Aucune route ouverte au client ne marque un lot
///     « Transmis ».
///   • L'appel est journalisé (JGK-H01) : transmettre l'état civil de toute une école est une
///     écriture sensible, même si techniquement c'est une lecture chez nous.
/// </summary>
public interface ISimenBridgeService
{
    /// <summary>
    /// Vrai quand un point d'accès est réellement configuré ET joignable. L'interface consomme ce
    /// drapeau AVANT de proposer le bouton « Transmettre au SIMEN » : proposer une action qui échouera
    /// à coup sûr n'est pas une fonctionnalité.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Transmet un lot d'élèves. Ne renvoie JAMAIS une réussite qu'elle n'a pas obtenue : sans relais
    /// configuré, le résultat porte <c>Accepted = false</c> et le motif — il n'y a pas de mode
    /// dégradé qui ferait semblant.
    /// </summary>
    Task<SimenTransmissionResult> TransmitStudentsAsync(
        PlaneteExportDto export, CancellationToken cancellationToken);

    /// <summary>
    /// Interroge le SIMEN pour obtenir les IEN OFFICIELS d'élèves déjà déclarés — le chemin qui rendra
    /// l'algorithme de secours inutile. La clé de rapprochement est le triplet (nom, date de naissance,
    /// lieu de naissance) faute d'identifiant commun préexistant : c'est fragile, et c'est
    /// précisément pourquoi la réponse doit être VALIDÉE par un humain avant d'écrire quoi que ce soit
    /// sur une fiche élève. Aucun Handler ne doit appliquer ce résultat automatiquement.
    /// </summary>
    Task<IReadOnlyList<SimenIenLookupResult>> LookupOfficialIensAsync(
        IReadOnlyList<SimenIenLookupRequest> candidates, CancellationToken cancellationToken);
}

/// <summary>
/// Résultat d'une transmission. <c>Accepted = false</c> avec un <c>FailureReason</c> renseigné est un
/// état NORMAL et attendu tant que le relais n'existe pas — jamais une exception, que les écrans
/// afficheraient comme une panne alors qu'il s'agit d'une fonctionnalité non ouverte.
/// </summary>
public record SimenTransmissionResult(
    bool Accepted,
    SimenSyncOutcome Outcome,
    string? RemoteBatchReference,
    string? FailureReason,
    int AcceptedCount,
    int RejectedCount)
{
    public static SimenTransmissionResult NotConfigured(string reason) =>
        new(false, SimenSyncOutcome.RelaisNonConfigure, null, reason, 0, 0);
}

/// <summary>Issue d'une tentative de transmission, du point de vue de l'appelant.</summary>
public enum SimenSyncOutcome
{
    /// <summary>Aucun point d'accès configuré — le cas actuel, et le seul atteignable aujourd'hui.</summary>
    RelaisNonConfigure,

    /// <summary>Lot accepté intégralement par le SIMEN.</summary>
    Accepte,

    /// <summary>Lot accepté partiellement : certaines lignes rejetées, détaillées par le SIMEN.</summary>
    AccepteAvecRejets,

    /// <summary>Lot rejeté (format, authentification, école inconnue).</summary>
    Rejete,

    /// <summary>Le SIMEN n'a pas répondu. À réessayer — jamais à interpréter comme un rejet.</summary>
    Injoignable
}

/// <summary>Un élève dont on cherche l'IEN officiel. Aucune donnée superflue n'est envoyée.</summary>
public record SimenIenLookupRequest(
    Guid StudentId,
    string LastName,
    string FirstNames,
    DateOnly BirthDate,
    string BirthPlace);

/// <summary>
/// Réponse du SIMEN pour un candidat. <c>Confidence</c> est renvoyé tel quel par le ministère et
/// NON réinterprété ici : c'est l'humain qui arbitre à l'écran, pas un seuil codé en dur qui
/// écraserait un jour l'IEN d'un homonyme.
/// </summary>
public record SimenIenLookupResult(
    Guid StudentId,
    string? OfficialIen,
    decimal Confidence,
    string? Notes);
