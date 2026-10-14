using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Subscriptions.Changes;

/// <summary>
/// E-mail adressé au Directeur quand le Super Admin décide sa demande de changement d'abonnement. Pur — aucun accès base, aucun
/// envoi : il ne fait que rédiger, pour se tester seul. Ne transmet jamais de mot de passe ni d'identifiant interne ; seulement ce
/// qui change, et pour un rejet le motif que le Super Admin a écrit POUR le Directeur.
/// </summary>
public static class SubscriptionChangeDecisionEmail
{
    public static EmailMessage Build(
        string? directorName,
        string to,
        IReadOnlyList<ChangeLine> changes,
        bool approved,
        string? reviewNote,
        string loginUrl)
    {
        var greeting = string.IsNullOrWhiteSpace(directorName) ? "Bonjour," : $"Bonjour {directorName.Trim()},";
        var note = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();

        var lines = changes.Count == 0
            ? string.Empty
            : string.Join("\n", changes.Select(c => $" - {c.From} → {c.To}")) + "\n\n";

        if (approved)
        {
            var body =
                $"""
                 {greeting}

                 Votre demande de changement d'abonnement a été validée. Voici ce qui a changé pour votre établissement :

                 {lines}Vos paramètres sont à jour : connectez-vous pour en profiter — {loginUrl}
                 """;

            if (note is not null)
            {
                body += $"\n\nMessage de notre équipe : {note}";
            }

            return new EmailMessage(to, "Votre demande de changement d'abonnement est validée", body);
        }

        var rejection =
            $"""
             {greeting}

             Votre demande de changement d'abonnement n'a pas été retenue.

             {(note is null ? string.Empty : $"Motif : {note}\n\n")}Votre abonnement reste inchangé. Pour en discuter ou envoyer une nouvelle demande, répondez à notre équipe ou reconnectez-vous — {loginUrl}
             """;

        return new EmailMessage(to, "Votre demande de changement d'abonnement n'a pas été retenue", rejection);
    }
}
