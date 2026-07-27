using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Envoi d'un SMS métier, gardes comprises (formule, commutateur d'école, solde, historique) — voir
/// SmsDispatcher pour le détail. Les déclencheurs dépendent de CE contrat, jamais d'
/// <see cref="ISmsService"/> directement : appeler l'agrégateur sans passer par les gardes enverrait
/// des SMS non facturés, ou hors de la formule souscrite.
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
/// Résultat NON EXCEPTIONNEL : un SMS non parti est une information, jamais une erreur qui doive
/// annuler l'opération métier l'ayant déclenché (saisie d'un retard, encaissement).
/// </summary>
public record SmsDispatchOutcome(bool IsSent, string? Reason)
{
    public static SmsDispatchOutcome Sent => new(true, null);

    /// <summary>Volontairement non envoyé : hors formule, alerte désactivée, sans numéro, solde épuisé.</summary>
    public static SmsDispatchOutcome Skipped(string reason) => new(false, reason);

    /// <summary>Tenté mais refusé par l'agrégateur.</summary>
    public static SmsDispatchOutcome Failed(string reason) => new(false, reason);
}
