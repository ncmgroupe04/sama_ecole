using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// MISE EN FILE d'un SMS métier, gardes comprises (formule, commutateur d'école, solde, historique)
/// — voir SmsDispatcher pour le détail. Les déclencheurs dépendent de CE contrat, jamais d'
/// <see cref="ISmsService"/> directement : appeler l'agrégateur sans passer par les gardes enverrait
/// des SMS non facturés, ou hors de la formule souscrite.
///
/// N'ATTEND PAS le fournisseur : la remise est faite hors requête par SmsQueueProcessor. Une relance
/// d'impayés sur 400 familles ne doit pas tenir la requête HTTP ouverte le temps de 400 allers-retours
/// réseau, ni perdre les 300 derniers messages parce que l'agrégateur est tombé au 100e.
/// </summary>
public interface ISmsDispatcher
{
    Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="SchoolId"/> est explicite et non déduit d'ITenantProvider : la plupart des envois
/// naissent dans un gestionnaire d'événement, où l'école pertinente est celle portée par l'événement.
/// </summary>
public record SmsDispatchRequest(
    Guid SchoolId,
    string? Recipient,
    string Body,
    SmsTrigger Trigger,
    Guid? StudentId = null);

/// <summary>
/// Résultat NON EXCEPTIONNEL : un SMS non mis en file est une information, jamais une erreur qui
/// doive annuler l'opération métier l'ayant déclenché (saisie d'un retard, encaissement).
///
/// Il n'existe volontairement PAS de valeur « envoyé » ici : au retour de DispatchAsync, le message
/// n'est qu'inscrit en file. Prétendre le contraire à l'appelant serait faux. Le sort réel de
/// l'envoi (Sent, Delivered, Failed) se lit dans l'historique, alimenté par le worker et le DLR.
/// </summary>
/// <param name="MessageId">
/// Identifiant de la ligne SmsMessage écrite pour cette tentative — null uniquement quand AUCUNE
/// ligne n'a été écrite (ex. aucun numéro de téléphone renseigné, seul cas qui retourne avant
/// l'historisation). Permet à un appelant qui orchestre plusieurs envois (ex.
/// SendDebtorReminderBatchCommand) de relier chaque destinataire à sa trace, sans réinterroger
/// l'historique par déduction.
/// </param>
public record SmsDispatchOutcome(bool IsQueued, string? Reason, Guid? MessageId = null)
{
    public static SmsDispatchOutcome Queued(Guid messageId) => new(true, null, messageId);

    /// <summary>Volontairement non mis en file : hors formule, alerte désactivée, sans numéro, solde épuisé.</summary>
    public static SmsDispatchOutcome Skipped(string reason, Guid? messageId = null) => new(false, reason, messageId);
}
