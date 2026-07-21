using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.FunctionalTests.Common;

/// <summary>
/// Capture les e-mails au lieu de les envoyer (ticket JGK-B01).
///
/// Permet de vérifier ce qu'on ne peut vérifier nulle part ailleurs : le mot de passe initial du
/// Directeur ne sort QUE par cet e-mail — jamais par la réponse HTTP. Le test s'en sert ensuite pour
/// se connecter réellement avec le compte fraîchement créé, ce qui prouve toute la chaîne.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public void Clear() => _sent.Clear();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage? LastTo(string email) =>
        _sent.LastOrDefault(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Extrait le jeton du lien de réinitialisation. Comme le mot de passe initial, le jeton en clair
    /// ne sort QUE par cet e-mail : la base n'en stocke que le SHA-256, et la réponse HTTP ne le
    /// contient pas. Sans cette extraction, le parcours self-service ne serait testable qu'à moitié.
    /// </summary>
    public static string ExtractResetToken(EmailMessage message)
    {
        var match = Regex.Match(message.Body, @"reinitialiser-mot-de-passe\?token=(?<token>\S+)");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Aucun lien de réinitialisation trouvé dans l'e-mail :\n{message.Body}");
        }

        return match.Groups["token"].Value;
    }

    /// <summary>Extrait le mot de passe provisoire du corps de l'e-mail.</summary>
    public static string ExtractPassword(EmailMessage message)
    {
        var match = Regex.Match(message.Body, @"Mot de passe\s*:\s*(?<password>\S+)");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Aucun mot de passe trouvé dans l'e-mail :\n{message.Body}");
        }

        return match.Groups["password"].Value;
    }
}
