namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Envoi d'un SMS chez l'agrégateur (Infobip/Orange/Twilio). Port volontairement minimal, au même
/// titre qu'<see cref="IEmailSender"/> : deux adaptateurs choisis par environnement (voir
/// SamaEcole.Infrastructure.DependencyInjection) — HttpSmsService (réel) et LoggingSmsService
/// (Development, journalise au lieu d'envoyer).
///
/// Ce port NE CONNAÎT NI le solde, ni les commutateurs d'activation, ni l'historique : tout cela est
/// du métier et vit dans SmsDispatcher (couche Application). Ici, on ne fait que parler au
/// fournisseur — c'est ce qui permet d'en changer sans toucher à une seule règle métier.
/// </summary>
public interface ISmsService
{
    /// <summary>Nom de l'agrégateur, journalisé et stocké dans l'historique.</summary>
    string ProviderName { get; }

    /// <summary>
    /// N'échoue JAMAIS par exception pour un refus du fournisseur : un SMS non parti ne doit pas
    /// faire échouer la saisie d'un retard ou l'encaissement qui l'a déclenché. L'échec est décrit
    /// dans le résultat, à charge de l'appelant de le journaliser.
    /// </summary>
    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken);
}

public record SmsSendRequest(string To, string Body);

public record SmsSendResult(bool IsSent, string? ProviderMessageId, string? FailureReason, int SegmentCount);
