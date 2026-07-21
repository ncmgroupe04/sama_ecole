using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.ResetPassword;

/// <summary>
/// Consomme un jeton de réinitialisation et fixe le nouveau mot de passe.
///
/// UN SEUL message d'erreur pour tous les échecs de jeton (inconnu, expiré, déjà consommé, révoqué,
/// compte devenu inactif) : distinguer « ce jeton a expiré » de « ce jeton n'existe pas » indiquerait à
/// un attaquant qu'il a deviné un condensat valide, et transformerait la route en oracle. Le message
/// unique invite simplement à refaire une demande, ce qui est l'action utile dans tous les cas.
///
/// Le mot de passe et la révocation des sessions passent par UNE transaction : un mot de passe changé
/// alors que les sessions restent ouvertes laisserait l'ancien détenteur connecté — précisément ce que
/// la victime d'un vol de compte cherche à couper.
/// </summary>
public class ResetPasswordCommandHandler(
    IAuthStore authStore,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ILogger<ResetPasswordCommandHandler> logger)
    : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public async Task<ResetPasswordResult> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var stored = await authStore.FindPasswordResetTokenAsync(
            PasswordResetTokenFactory.Hash(request.Token), cancellationToken);

        if (stored is null || !stored.IsUsable(now))
        {
            logger.LogWarning("Tentative de réinitialisation avec un jeton invalide, expiré ou déjà utilisé.");
            throw InvalidToken();
        }

        // Le compte a pu être suspendu ou bloqué entre l'émission du lien et son usage : le jeton ne
        // doit pas rouvrir un accès que le Directeur vient de fermer (ticket JGK-A05).
        var user = await authStore.FindUserByIdAsync(stored.UserId, cancellationToken);

        if (user is null || user.Status != EntityStatus.Active)
        {
            logger.LogWarning(
                "Jeton de réinitialisation valide présenté pour le compte {UserId}, devenu inactif depuis son émission.",
                stored.UserId);
            throw InvalidToken();
        }

        // Nouveau mot de passe, jeton consommé, demandes concurrentes périmées et sessions révoquées —
        // en UNE transaction, côté store (voir IAuthStore.CompletePasswordResetAsync). Même geste que
        // la voie administrative (ResetUserPasswordCommandHandler) quant aux sessions.
        var revokedSessions = await authStore.CompletePasswordResetAsync(
            stored.Id, user.Id, passwordHasher.Hash(request.NewPassword), cancellationToken);

        logger.LogInformation(
            "Mot de passe réinitialisé en self-service pour le compte {UserId}. {Revoked} session(s) révoquée(s).",
            user.Id, revokedSessions);

        return new ResetPasswordResult(revokedSessions);
    }

    /// <summary>
    /// Message UNIQUE, volontairement peu précis — voir la remarque de classe. Porté par le champ Token
    /// pour que l'écran l'affiche au bon endroit (docs/Volume_4_API_Design.md §0.4).
    /// </summary>
    private static ValidationException InvalidToken() => new([
        new ValidationFailure(
            nameof(ResetPasswordCommand.Token),
            "Ce lien de réinitialisation n'est plus valable. Refaites une demande depuis la page de connexion.")
    ]);
}
