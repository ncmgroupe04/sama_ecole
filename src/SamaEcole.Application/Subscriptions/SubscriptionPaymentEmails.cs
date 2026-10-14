using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// E-mails adressés au Directeur à l'issue d'un paiement d'abonnement : partagés par le webhook de l'agrégateur
/// (ProcessPaymentWebhookHandler) et la validation manuelle d'un paiement Wave (ReviewWavePaymentCommandHandler),
/// pour que les deux parcours disent exactement la même chose.
/// </summary>
public static class SubscriptionPaymentEmails
{
    public static Task SendActivationAsync(
        IEmailSender emailSender, SubscriptionPaymentConfirmation result, CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {result.DirectorFullName},

             Votre paiement a été confirmé : l'abonnement de « {result.SchoolName} » est maintenant actif.

             Vous avez désormais accès à l'ensemble des modules de Unikol (élèves, classes, notes,
             finances…). Votre abonnement est valable jusqu'au {result.NewExpiresAt:dd/MM/yyyy}.
             """;

        return emailSender.SendAsync(
            new EmailMessage(result.DirectorEmail, "Votre abonnement Unikol est actif", body),
            cancellationToken);
    }

    public static Task SendWaveRejectionAsync(
        IEmailSender emailSender, SubscriptionPaymentConfirmation result, decimal amount, string reference,
        CancellationToken cancellationToken)
    {
        var body =
            $"""
             Bonjour {result.DirectorFullName},

             Nous n'avons pas pu rapprocher votre paiement Wave de {amount:N0} FCFA (référence {reference})
             pour l'abonnement de « {result.SchoolName} » : aucun règlement correspondant n'apparaît sur notre compte.

             Votre abonnement n'est donc pas activé. Si vous avez bien payé, répondez à cet e-mail en joignant la
             capture du reçu Wave ; sinon, relancez le paiement depuis votre espace Unikol.
             """;

        return emailSender.SendAsync(
            new EmailMessage(result.DirectorEmail, "Paiement Wave non retrouvé — abonnement Unikol", body),
            cancellationToken);
    }
}
