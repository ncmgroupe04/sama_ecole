using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.ForgotPassword;

/// <summary>
/// Émet un lien de réinitialisation — ou ne fait rien, sans que l'appelant puisse faire la différence.
///
/// ANTI-ÉNUMÉRATION DE COMPTES : cette route est anonyme et publique. Si elle répondait « adresse
/// inconnue » d'un côté et « e-mail envoyé » de l'autre, elle deviendrait un oracle permettant de
/// tester des adresses en masse pour dresser la liste des comptes existants — matière première d'une
/// campagne de hameçonnage ciblée ou de credential stuffing. Le contrat est donc : réponse IDENTIQUE
/// dans tous les cas, aucune exception levée, aucun code d'erreur distinct. La limitation de débit par
/// IP (AuthController) borne en plus le volume de tests possibles.
///
/// Le même raisonnement vaut pour les comptes suspendus ou bloqués : ne pas envoyer d'e-mail, mais ne
/// rien en dire non plus. Rendre l'accès à un compte désactivé n'aurait aucun sens, et le signaler
/// renseignerait l'attaquant sur l'état du compte.
/// </summary>
public class ForgotPasswordCommandHandler(
    IAuthStore authStore,
    IEmailSender emailSender,
    AuthSettings authSettings,
    TimeProvider timeProvider,
    ILogger<ForgotPasswordCommandHandler> logger)
    : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();

        // users est sous RLS et l'appelant n'a aucun tenant : la lecture passe par la fonction
        // SECURITY DEFINER du chemin de login, jamais par une requête EF (voir IAuthStore).
        var user = await authStore.FindUserByEmailAsync(email, cancellationToken);

        if (user is null || user.Status != EntityStatus.Active)
        {
            // Journalisé au niveau Information et SANS l'adresse saisie : la trace sert à repérer un
            // balayage (volume anormal), pas à constituer une liste d'adresses testées dans les logs.
            logger.LogInformation(
                "Demande de réinitialisation sans suite (compte inexistant ou inactif). Aucune information renvoyée à l'appelant.");
            return;
        }

        var token = PasswordResetTokenFactory.Create();
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(authSettings.PasswordResetMinutes);

        await authStore.StorePasswordResetTokenAsync(
            user.Id, PasswordResetTokenFactory.Hash(token), expiresAt, cancellationToken);

        var link = $"{authSettings.PublicBaseUrl.TrimEnd('/')}/reinitialiser-mot-de-passe?token={token}";

        await emailSender.SendAsync(
            new EmailMessage(
                To: user.Email,
                Subject: "Réinitialisation de votre mot de passe Sama Ecole",
                Body: $"""
                       Bonjour {user.FullName},

                       Vous avez demandé à réinitialiser le mot de passe de votre compte Sama Ecole.
                       Cliquez sur le lien ci-dessous pour choisir un nouveau mot de passe :

                       {link}

                       Ce lien est valable {authSettings.PasswordResetMinutes} minutes et ne peut servir qu'une seule fois.

                       Si vous n'êtes pas à l'origine de cette demande, ignorez ce message : votre mot de
                       passe actuel reste valable et aucune modification n'a été faite sur votre compte.

                       L'équipe Sama Ecole
                       """),
            cancellationToken);

        logger.LogInformation("Lien de réinitialisation émis pour le compte {UserId}.", user.Id);
    }
}
